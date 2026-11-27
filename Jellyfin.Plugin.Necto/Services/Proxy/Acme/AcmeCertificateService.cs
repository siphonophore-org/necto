using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Siphonophore.Stolon;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Acme;

/// <summary>
/// Gets and renews the proxy's certificate from Let's Encrypt. When every name falls under a DDNS record whose
/// provider can publish TXT records, the names are proved over DNS-01, which needs no open port and covers
/// wildcards. Otherwise HTTP-01 is used: Let's Encrypt fetches a token from
/// <c>http://name/.well-known/acme-challenge/</c> on port 80, which the router forwards to the proxy's HTTP
/// listener, and <see cref="Http01ChallengeStore"/> answers it there. The issued chain and key are written as PEM
/// files that <see cref="CertificateStore"/> loads like any other PEM certificate. Setting the
/// <c>DDNS_ACME_DIRECTORY</c> environment variable points it at another ACME server, such as Let's Encrypt's
/// staging server for testing.
/// </summary>
public sealed class AcmeCertificateService : IDisposable
{
    private const string DirectoryVariable = "NECTO_ACME_DIRECTORY";
    private static readonly TimeSpan _validationPoll = TimeSpan.FromSeconds(2);
    private const int ValidationPolls = 60;
    private static readonly Uri _dnsOverHttps = new("https://cloudflare-dns.com/dns-query");

    private readonly ProxyPaths _paths;
    private readonly Http01ChallengeStore _challenges;
    private readonly IHttpClientFactory _http;
    private readonly IReadOnlyList<IDNSProvider> _providers;
    private readonly SecretProtector? _secrets;
    private readonly ActivityLogger? _activity;
    private readonly INotifier? _notifier;
    private readonly ILogger<AcmeCertificateService> _logger;
    private readonly JsonFileStore<PendingTxtList> _pendingTxt;
    private readonly object _pendingLock = new();

    // A TXT record that still cannot be removed after this long is given up on and reported once.
    private static readonly TimeSpan _txtGiveUp = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _issuing = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="AcmeCertificateService"/> class.
    /// </summary>
    /// <param name="paths">The proxy's data paths.</param>
    /// <param name="challenges">Where HTTP-01 answers are published for the proxy to serve.</param>
    /// <param name="http">The HTTP client factory, for talking to Let's Encrypt.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="providers">The DNS providers, some of which can publish TXT records for DNS-01.</param>
    /// <param name="secrets">Decrypts the DDNS records' credentials.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="notifier">Sends issued and failed certificates to Notifications.</param>
    public AcmeCertificateService(
        ProxyPaths paths,
        Http01ChallengeStore challenges,
        IHttpClientFactory http,
        ILogger<AcmeCertificateService> logger,
        IEnumerable<IDNSProvider>? providers = null,
        SecretProtector? secrets = null,
        ActivityLogger? activity = null,
        INotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _providers = (providers ?? Enumerable.Empty<IDNSProvider>()).ToList();
        _secrets = secrets;
        _paths = paths;
        _challenges = challenges;
        _http = http;
        _logger = logger;
        _activity = activity;
        _notifier = notifier;
        _pendingTxt = new JsonFileStore<PendingTxtList>(paths.AcmeTxtCleanupFile, logger);
    }

    /// <summary>Gets or sets how many times public DNS is asked for a published TXT record.</summary>
    internal int TxtPolls { get; set; } = 24;

    /// <summary>Gets or sets the pause before each TXT lookup.</summary>
    internal TimeSpan TxtPollDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long one TXT lookup may take.</summary>
    internal TimeSpan TxtLookupTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the ACME directory in use: Let's Encrypt, unless overridden for testing.</summary>
    public static Uri DirectoryUri
        => Uri.TryCreate(Environment.GetEnvironmentVariable(DirectoryVariable), UriKind.Absolute, out var custom) ? custom : WellKnownServers.LetsEncryptV2;

    /// <summary>
    /// Describes the current state for the dashboard.
    /// </summary>
    /// <param name="request">The route's files, names and challenge.</param>
    /// <param name="records">The DDNS records, for DNS-01.</param>
    /// <returns>The status.</returns>
    public AcmeStatus Describe(AcmeRequest request, IEnumerable<DNSRecord> records)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = AcmePlanner.Normalize(Store(request.Slot).Load(), DateTime.UtcNow);
        var plan = Plan(request, records);

        // While a run is going, the attempt recorded at its start is not a failure to report.
        var issuing = _issuing.ContainsKey(request.Slot.Directory);
        return new AcmeStatus
        {
            Issuing = issuing,
            LastError = state.ConsecutiveFailures > 0 && !issuing ? state.LastError : string.Empty,
            LastSuccessUtc = state.LastSuccessUtc,
            NextAttemptUtc = !issuing && state.ConsecutiveFailures > 0 && state.LastAttemptUtc is { } last ? last + AcmePlanner.Backoff(state.ConsecutiveFailures) : null,
            RateLimited = !issuing && state.ConsecutiveFailures > 0 && state.RateLimited,
            Names = plan.Names,
            Method = plan.Method,
            DnsProviders = plan.Records.Values.Select(r => ProviderLabel(r.Provider)).Distinct(StringComparer.Ordinal).ToList()
        };
    }

    /// <summary>
    /// Gets or renews the certificate when the planner says so. Only one run happens at a time; a call while
    /// one is running returns at once.
    /// </summary>
    /// <param name="request">The route's files, names and challenge.</param>
    /// <param name="records">The DDNS records, for DNS-01.</param>
    /// <param name="current">The loaded certificate's status.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <param name="force">
    /// Orders a new certificate whatever the planner says, for an administrator who asked for one: after a key
    /// was exposed, or to replace one that is wrong in a way the planner cannot see. Let's Encrypt's own rate
    /// limits still apply.
    /// </param>
    /// <returns>True when a new certificate was written.</returns>
    public async Task<bool> EnsureAsync(AcmeRequest request, IEnumerable<DNSRecord> records, CertificateStatus current, CancellationToken cancellationToken, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        if (!await _run.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            var recordList = records.ToList();
            await CleanUpLeftoverTxtAsync(recordList).ConfigureAwait(false);
            var plan = Plan(request, recordList);
            var names = plan.Names;
            var store = Store(request.Slot);
            var state = store.Load();
            var normalized = AcmePlanner.Normalize(state, DateTime.UtcNow);
            if (!ReferenceEquals(normalized, state))
            {
                store.Save(normalized);
                state = normalized;
            }

            var loadedNames = current.Loaded ? current.Names : new List<string>();

            // A certificate from another directory, such as a staging one kept after testing, is replaced as if
            // missing: it covers the names but nothing trusts it.
            var directory = DirectoryUri.ToString();
            if (state.Directory.Length > 0 && !string.Equals(state.Directory, directory, StringComparison.OrdinalIgnoreCase))
            {
                loadedNames = new List<string>();
            }

            var decision = AcmePlanner.Decide(state, names, loadedNames, current.Loaded ? current.NotAfterUtc : null, DateTime.UtcNow, current.Loaded ? current.NotBeforeUtc : null, directory);

            // A request past the timing rules still waits out Let's Encrypt's rate limit: asking during it only
            // fails again and counts against the limit.
            if (force && decision.Action != AcmeAction.NoNames && !InRateLimit(state, DateTime.UtcNow))
            {
                decision = new AcmeDecision(current.Loaded ? AcmeAction.Renew : AcmeAction.Issue);
            }

            if (decision.Action is not (AcmeAction.Issue or AcmeAction.Renew))
            {
                return false;
            }

            _issuing[request.Slot.Directory] = 0;
            var started = DateTime.UtcNow;

            // Counted as a failure until it finishes, so an attempt cut short by a shutdown still backs off: Let's
            // Encrypt has counted the order either way. The outcome below replaces this record.
            store.Save(state.Failed(started, ServerText.Get(TranslationKeys.ProxyErrorAcmeUnfinished), names));
            try
            {
                var notBefore = await IssueAsync(plan, request.Slot, cancellationToken).ConfigureAwait(false);
                store.Save(AcmeState.Succeeded(started, names, directory, notBefore));
                var list = string.Join(", ", names);
                _logger.LogInformation("Let's Encrypt issued a certificate for {Names}", list);
                if (decision.Action == AcmeAction.Renew)
                {
                    _activity?.Log(ServerText.Get(TranslationKeys.ProxyActivityCertificateRenewed), "Necto.Proxy.CertificateRenewed", ServerText.Format(TranslationKeys.ProxyActivityCertificateRenewedOverview, list));
                }
                else
                {
                    _activity?.Log(ServerText.Get(TranslationKeys.ProxyActivityCertificateIssued), "Necto.Proxy.CertificateIssued", ServerText.Format(TranslationKeys.ProxyActivityCertificateIssuedOverview, list));
                }

                if (_notifier is not null)
                {
                    await _notifier.SendAsync(new NectoEvent(ProxyEvents.CertificateIssued, ServerText.Format(TranslationKeys.ProxyEventCertificateIssuedTitle, list), ServerText.Format(TranslationKeys.ProxyActivityCertificateIssuedOverview, list)), CancellationToken.None).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var error = CredentialRedactor.Redact(Explain(ex));
                var failed = state.Failed(started, error, names, IsRateLimit(ex));
                store.Save(failed);
                var next = started + AcmePlanner.Backoff(failed.ConsecutiveFailures);
                // A failed renewal still has a working certificate, so it is a warning. Without one, HTTPS is down.
                var level = decision.Action == AcmeAction.Renew ? LogLevel.Warning : LogLevel.Error;
                _logger.Log(level, "Let's Encrypt did not issue a certificate for {Names}: {Error}", string.Join(", ", names), error);
                _activity?.Log(
                    ServerText.Get(decision.Action == AcmeAction.Renew ? TranslationKeys.ProxyActivityRenewalFailed : TranslationKeys.ProxyActivityRequestFailed),
                    decision.Action == AcmeAction.Renew ? "Necto.Proxy.CertificateRenewFailed" : "Necto.Proxy.CertificateIssueFailed",
                    ActivityEntries.Plain(ServerText.Format(TranslationKeys.ProxyActivityNextTryAfter, ActivityEntries.Phrase(error), next.ToString("u", System.Globalization.CultureInfo.InvariantCulture))),
                    level);
                if (_notifier is not null)
                {
                    await _notifier.SendAsync(new NectoEvent(ProxyEvents.CertificateFailed, ServerText.Format(TranslationKeys.ProxyEventCertificateFailedTitle, string.Join(", ", names)), error) { Level = decision.Action == AcmeAction.Renew ? NectoEventLevel.Warning : NectoEventLevel.Error }, CancellationToken.None).ConfigureAwait(false);
                }

                return false;
            }
            finally
            {
                _issuing.TryRemove(request.Slot.Directory, out _);
            }
        }
        finally
        {
            _run.Release();
        }
    }

    /// <summary>
    /// When the certificate should next be looked at, for the retry timer: the end of a wait, or the loaded
    /// certificate's renewal time.
    /// </summary>
    /// <param name="request">The route's files, names and challenge.</param>
    /// <param name="records">The DDNS records, for DNS-01.</param>
    /// <param name="current">The loaded certificate's status.</param>
    /// <returns>The time (UTC), or null when there is nothing to order.</returns>
    public DateTime? NextCheckUtc(AcmeRequest request, IEnumerable<DNSRecord> records, CertificateStatus current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        var now = DateTime.UtcNow;
        var state = AcmePlanner.Normalize(Store(request.Slot).Load(), now);
        var directory = DirectoryUri.ToString();
        var loadedNames = current.Loaded && (state.Directory.Length == 0 || string.Equals(state.Directory, directory, StringComparison.OrdinalIgnoreCase))
            ? current.Names
            : new List<string>();
        return AcmePlanner.NextCheckUtc(state, Plan(request, records).Names, loadedNames, current.Loaded ? current.NotAfterUtc : null, now, current.Loaded ? current.NotBeforeUtc : null, directory);
    }

    /// <inheritdoc />
    public void Dispose() => _run.Dispose();

    /// <summary>
    /// Whether Let's Encrypt's rate limit refused the last attempt and its backoff has not run out yet.
    /// </summary>
    /// <param name="state">The stored state.</param>
    /// <param name="now">The current time (UTC).</param>
    /// <returns>True while a request must wait.</returns>
    internal static bool InRateLimit(AcmeState state, DateTime now)
        => state.RateLimited && state.ConsecutiveFailures > 0 && state.LastAttemptUtc is { } last && now < last + AcmePlanner.Backoff(state.ConsecutiveFailures);

    /// <summary>
    /// Whether an ACME error is Let's Encrypt's rate limit.
    /// </summary>
    /// <param name="ex">The error.</param>
    /// <returns>True for a rate limit.</returns>
    internal static bool IsRateLimit(Exception ex)
        => ex is AcmeRequestException { Error: { } error }
            && ((error.Type?.ToString() ?? string.Empty).Contains("rateLimited", StringComparison.OrdinalIgnoreCase) || error.Status == System.Net.HttpStatusCode.TooManyRequests);

    // Let's Encrypt's own message says what it could not reach; the rest are network or file problems.
    private static string Explain(Exception ex) => ex switch
    {
        AcmeRequestException { Error.Detail: { Length: > 0 } detail } => detail,
        TaskCanceledException => ServerText.Get(TranslationKeys.ProxyErrorAcmeTimeout),
        _ => ex.Message
    };

    private AcmePlan Plan(AcmeRequest request, IEnumerable<DNSRecord> records)
        => AcmePlan.For(request.Hostnames, records, kind => _providers.Any(p => p.Kind == kind && p is IAcmeDnsProvider), request.Challenge, request.DnsRecordId);

    // Each route keeps its own record of attempts, so one route's failures never hold back another's certificate.
    private JsonFileStore<AcmeState> Store(CertificatePaths slot)
    {
        System.IO.Directory.CreateDirectory(slot.Directory);
        return new JsonFileStore<AcmeState>(slot.AcmeStateFile, _logger);
    }

    private string ProviderLabel(DNSProviderKind kind) => _providers.FirstOrDefault(p => p.Kind == kind)?.Label ?? kind.ToString();

    private async Task<DateTime> IssueAsync(AcmePlan plan, CertificatePaths slot, CancellationToken cancellationToken)
    {
        var names = plan.Names;
        System.IO.Directory.CreateDirectory(_paths.AcmeDirectory);
        var directory = DirectoryUri;
        var acme = new AcmeContext(directory, LoadOrCreateAccountKey(), new AcmeHttpClient(directory, _http.CreateClient(nameof(AcmeCertificateService))));

        // Asking to create an account with an existing key returns that account.
        await acme.NewAccount(Array.Empty<string>(), termsOfServiceAgreed: true).ConfigureAwait(false);
        var order = await acme.NewOrder(names).ConfigureAwait(false);

        var published = new List<string>();
        try
        {
            foreach (var authorization in await order.Authorizations().ConfigureAwait(false))
            {
                var resource = await authorization.Resource().ConfigureAwait(false);
                if (resource.Status == AuthorizationStatus.Valid)
                {
                    continue;
                }

                var identifier = resource.Identifier?.Value ?? string.Empty;
                if (plan.Method == AcmeMethod.Dns)
                {
                    var name = resource.Wildcard == true ? "*." + identifier : identifier;
                    await ProveOverDnsAsync(acme, authorization, plan, name, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var challenge = await authorization.Http().ConfigureAwait(false);
                _challenges.Add(challenge.Token, challenge.KeyAuthz);
                published.Add(challenge.Token);
                await challenge.Validate().ConfigureAwait(false);
                await WaitForValidationAsync(authorization, identifier, cancellationToken).ConfigureAwait(false);
            }

            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
            // Let's Encrypt refuses a common name over 64 characters; the names stay in the certificate either way.
            var chain = await order.Generate(new CsrInfo { CommonName = names.FirstOrDefault(n => n.Length <= 64) }, key).ConfigureAwait(false);
            // The key also goes into the chain file, which is replaced in one rename, so a crash or a read between
            // two writes never pairs a new chain with the old key. The separate key file is written too, for tools that read it.
            var keyPem = key.ToPem();
            System.IO.Directory.CreateDirectory(slot.Directory);
            WriteSecret(slot.AcmeKeyFile, keyPem);
            WriteSecret(slot.AcmeChainFile, chain.ToPem().TrimEnd() + "\n" + keyPem);

            // Only a certificate the proxy can serve counts as issued. One that does not load is recorded as a
            // failure, so it backs off instead of being ordered again at every check.
            if (CertificateStore.PemLoadError(slot.AcmeChainFile) is { } problem)
            {
                _logger.LogWarning("The certificate Let's Encrypt issued could not be loaded: {Problem}", problem);
                throw new InvalidOperationException(ServerText.Get(TranslationKeys.ProxyErrorAcmeFilesUnloadable));
            }

            using var leaf = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(chain.Certificate.ToPem());
            return leaf.NotBefore.ToUniversalTime();
        }
        finally
        {
            foreach (var token in published)
            {
                _challenges.Remove(token);
            }
        }
    }

    // One name at a time, so a provider that keeps a single TXT value per domain (DuckDNS) still works for a
    // name and its wildcard. The record is removed whether or not Let's Encrypt accepted it.
    private async Task ProveOverDnsAsync(AcmeContext acme, IAuthorizationContext authorization, AcmePlan plan, string name, CancellationToken cancellationToken)
    {
        if (!plan.Records.TryGetValue(name, out var record) || _providers.FirstOrDefault(p => p.Kind == record.Provider) is not IAcmeDnsProvider provider)
        {
            throw new InvalidOperationException(ServerText.Format(TranslationKeys.ProxyErrorNoTxtRecord, name));
        }

        var live = WithSecrets(record);
        var challenge = await authorization.Dns().ConfigureAwait(false);
        var txtName = AcmePlan.TxtName(name);
        var value = acme.AccountKey.DnsTxt(challenge.Token);
        // Noted before it is published, so a crash right after still leaves a record of what to remove.
        var pending = new PendingTxt { RecordId = record.Id, Name = txtName, Value = value, PublishedUtc = DateTime.UtcNow };
        ChangePending(list => list.Records.Add(pending));
        // A refusal may still have published the record, for example when the provider timed out answering. The
        // entry stays so the cleanup removes it, which is harmless when the record is not there.
        var (ok, message) = await provider.SetTxtAsync(live, txtName, value, cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            // The provider's reply goes on to the log, the activity log and the page, so the credentials it was
            // sent with are masked in it first.
            throw new InvalidOperationException(CredentialRedactor.Redact(message, new[] { live.Login, live.Password }));
        }

        try
        {
            await WaitForTxtAsync(txtName, value, cancellationToken).ConfigureAwait(false);
            await challenge.Validate().ConfigureAwait(false);
            await WaitForValidationAsync(authorization, name, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (await TryRemoveAsync(provider, live, pending).ConfigureAwait(false))
            {
                ChangePending(list => list.Forget(pending));
            }
            else
            {
                _logger.LogDebug("Could not remove the TXT record {Name}, it is tried again on the next certificate check", txtName);
            }
        }
    }

    /// <summary>
    /// Removes TXT records left behind by an earlier run: one whose removal failed, or one published just before
    /// a crash or restart. Each is tried on every check until it is gone, for at most a week.
    /// </summary>
    /// <param name="records">The DDNS records, whose credentials remove them.</param>
    /// <returns>A task.</returns>
    public async Task CleanUpLeftoverTxtAsync(IEnumerable<DNSRecord> records)
    {
        List<PendingTxt> pending;
        lock (_pendingLock)
        {
            pending = _pendingTxt.Load().Records.ToList();
        }

        if (pending.Count == 0)
        {
            return;
        }

        var byId = records.Where(r => !string.IsNullOrEmpty(r.Id)).GroupBy(r => r.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var leftOver = new ActivityBatch();
        foreach (var item in pending)
        {
            var removed = byId.TryGetValue(item.RecordId, out var record)
                && _providers.FirstOrDefault(p => p.Kind == record.Provider) is IAcmeDnsProvider provider
                && TryWithSecrets(record) is { } live
                && await TryRemoveAsync(provider, live, item).ConfigureAwait(false);
            if (removed)
            {
                _logger.LogDebug("Removed the leftover TXT record {Name}", item.Name);
            }
            else if (DateTime.UtcNow - item.PublishedUtc < _txtGiveUp && record is not null)
            {
                continue;
            }
            else
            {
                _logger.LogWarning("Gave up removing the TXT record {Name}, remove it at your DNS provider", item.Name);
                leftOver.Add(ActivityEntries.TxtRecordsLeftOver, item.Name);
            }

            ChangePending(list => list.Forget(item));
        }

        leftOver.Write(_activity);
    }

    // A removal must never stop the run it cleans up after, and is bounded so a hung provider cannot hold it.
    private async Task<bool> TryRemoveAsync(IAcmeDnsProvider provider, DNSRecord live, PendingTxt item)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            return await provider.RemoveTxtAsync(live, item.Name, item.Value, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or IOException)
        {
            _logger.LogDebug("Removing the TXT record {Name} failed: {Message}", item.Name, CredentialRedactor.Redact(ex.Message, new[] { live.Login, live.Password }));
            return false;
        }
    }

    private void ChangePending(Action<PendingTxtList> change)
    {
        lock (_pendingLock)
        {
            var list = _pendingTxt.Load();
            change(list);
            _pendingTxt.Save(list);
        }
    }

    // Credentials that no longer decrypt leave that one entry for a later try: the rest of the cleanup, and the
    // certificate check after it, go on.
    private DNSRecord? TryWithSecrets(DNSRecord record)
    {
        try
        {
            return WithSecrets(record);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug("Cannot remove a leftover TXT record for {Record} yet: {Message}", record.Name, CredentialRedactor.Redact(ex.Message));
            return null;
        }
    }

    private DNSRecord WithSecrets(DNSRecord record)
    {
        var login = _secrets?.Unprotect(record.Login) ?? record.Login;
        var password = _secrets?.Unprotect(record.Password) ?? record.Password;
        if ((SecretProtector.IsProtected(record.Login) && login.Length == 0) || (SecretProtector.IsProtected(record.Password) && password.Length == 0))
        {
            throw new InvalidOperationException(ServerText.Get(TranslationKeys.ProxyErrorRecordCredentialsUnreadable));
        }

        return record.WithSecrets(login, password);
    }

    // Let's Encrypt looks the record up once, so wait until public DNS answers with it. If the lookup itself
    // keeps failing, go ahead after the wait rather than never trying.
    // Each lookup is bounded on its own, so one that hangs costs a poll rather than the whole wait. A lookup that
    // times out, from this bound or the client's own timeout, is a failed lookup, not a cancelled run.
    internal async Task WaitForTxtAsync(string name, string value, CancellationToken cancellationToken)
    {
        using var client = _http.CreateClient(nameof(AcmeCertificateService));
        for (var i = 0; i < TxtPolls; i++)
        {
            await Task.Delay(TxtPollDelay, cancellationToken).ConfigureAwait(false);
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lookup.CancelAfter(TxtLookupTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _dnsOverHttps + "?name=" + Uri.EscapeDataString(name) + "&type=TXT");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, lookup.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(lookup.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode && TxtAnswers(body).Contains(value, StringComparer.Ordinal))
                {
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogDebug("Checking the TXT record {Name} failed: {Message}", name, CredentialRedactor.Redact(ex.Message));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Checking the TXT record {Name} timed out", name);
            }
        }

        _logger.LogDebug("The TXT record {Name} did not show up in public DNS in time, asking Let's Encrypt anyway", name);
    }

    /// <summary>
    /// Reads the TXT values out of a DNS-over-HTTPS JSON answer.
    /// </summary>
    /// <param name="body">The JSON answer.</param>
    /// <returns>The values, without their quotes.</returns>
    internal static List<string> TxtAnswers(string body)
    {
        var values = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("Answer", out var answers) && answers.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                values.AddRange(answers.EnumerateArray()
                    .Select(answer => answer.TryGetProperty("data", out var data) ? data.GetString() : null)
                    .OfType<string>()
                    .Select(text => text.Trim().Trim('"')));
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // No answers.
        }

        return values;
    }

    private static async Task WaitForValidationAsync(IAuthorizationContext authorization, string name, CancellationToken cancellationToken)
    {
        for (var i = 0; i < ValidationPolls; i++)
        {
            await Task.Delay(_validationPoll, cancellationToken).ConfigureAwait(false);
            var resource = await authorization.Resource().ConfigureAwait(false);
            switch (resource.Status)
            {
                case AuthorizationStatus.Valid:
                    return;
                case AuthorizationStatus.Pending:
                    continue;
                default:
                    var detail = resource.Challenges?.Select(c => c.Error?.Detail).FirstOrDefault(d => !string.IsNullOrEmpty(d));
                    throw new InvalidOperationException(detail ?? ServerText.Format(TranslationKeys.ProxyErrorAcmeCheckFailed, name));
            }
        }

        throw new InvalidOperationException(ServerText.Format(TranslationKeys.ProxyErrorAcmeCheckUnfinished, name));
    }

    /// <summary>
    /// Loads the Let's Encrypt account key, making one only when there is none. A key file that cannot be read
    /// right now, such as one locked by a backup, fails this attempt and is read again on the next. Only a file
    /// that was read and holds no usable key is set aside and replaced: a new key is a new account, which is
    /// harmless, while keeping the bad one would stop every renewal.
    /// </summary>
    /// <returns>The key.</returns>
    internal IKey LoadOrCreateAccountKey()
    {
        var path = _paths.AcmeAccountKeyFile;
        if (File.Exists(path))
        {
            string pem;
            try
            {
                pem = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "The Let's Encrypt account key could not be read");
                throw new IOException(ServerText.Get(TranslationKeys.ProxyErrorAcmeAccountKey), ex);
            }

            try
            {
                return KeyFactory.FromPem(pem);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException or FormatException or IOException or System.Security.Cryptography.CryptographicException)
            {
                // The file was read, so this is its content, not a passing read error: the key parser reports bad
                // PEM as an IOException too. Kept beside, in case it is wanted, rather than overwritten.
                _logger.LogWarning(ex, "The Let's Encrypt account key is damaged and is being replaced");
                File.Move(path, path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture), overwrite: true);
            }
        }

        var key = KeyFactory.NewKey(KeyAlgorithm.ES256);
        WriteSecret(path, key.ToPem());
        return key;
    }

    private static void WriteSecret(string path, string contents)
        => AtomicFile.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(contents), ownerOnly: true);
}
