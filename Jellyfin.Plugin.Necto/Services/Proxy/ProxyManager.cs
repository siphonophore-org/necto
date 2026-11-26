using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Owns the running proxy: starts it from the saved routes and settings, applies changes live when it can and
/// rebuilds the host when it must, keeps every route's certificate current, and reports status. All lifecycle
/// changes are serialized.
/// </summary>
public sealed class ProxyManager : IAsyncDisposable
{
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(5);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProxyManager> _logger;
    private readonly ProxyPaths _paths;
    private readonly INetworkManager _network;
    private readonly CertificateSet _certificates;
    private readonly Http01ChallengeStore _challenges;
    private readonly AccessRecorder _access;
    private readonly BlockFilter _blocks;
    private readonly BanList _bans;
    private readonly GeoIpService _geo;
    private readonly AcmeCertificateService _acme;
    private readonly JellyfinNetworkView _jellyfin;
    private readonly IConfigurationManager _configuration;
    private readonly Timer _acmeRetry;
    private readonly string _systemId;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ActivityLogger? _activity;
    private readonly LocalTrafficWatch _localTraffic;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ReachabilityChecker _reachability;
    private readonly NameChecker _names;
    private ProxyHost? _host;
    private ProxyRuntimeHolder? _holder;
    private DateTime? _startedUtc;
    private string _error = string.Empty;

    // The start failure last written to the activity log, so a proxy that keeps failing the same way is one
    // entry, and its recovery another.
    private string _reportedError = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyManager"/> class.
    /// </summary>
    /// <param name="loggerFactory">The logger factory, shared with the proxy host.</param>
    /// <param name="paths">The proxy data paths.</param>
    /// <param name="configuration">Jellyfin's configuration manager.</param>
    /// <param name="network">Jellyfin's network manager, for the LAN test.</param>
    /// <param name="application">The server host, for the ports Jellyfin is using and its id.</param>
    /// <param name="certificates">Every route's certificate.</param>
    /// <param name="challenges">Pending HTTP-01 challenges.</param>
    /// <param name="access">Where the proxy reports the address each client used.</param>
    /// <param name="blocks">The block list and country rule filter.</param>
    /// <param name="bans">The addresses banned for failed sign-ins.</param>
    /// <param name="geo">The location lookup, loaded when country rules need it.</param>
    /// <param name="acme">Gets and renews certificates from Let's Encrypt.</param>
    /// <param name="reachability">Checks the HTTPS port from outside the network.</param>
    /// <param name="addresses">This server's public addresses, for the name check.</param>
    /// <param name="activity">The activity log writer, for start failures and recovery.</param>
    public ProxyManager(
        ILoggerFactory loggerFactory,
        ProxyPaths paths,
        IConfigurationManager configuration,
        INetworkManager network,
        IServerApplicationHost application,
        CertificateSet certificates,
        Http01ChallengeStore challenges,
        AccessRecorder access,
        BlockFilter blocks,
        BanList bans,
        GeoIpService geo,
        AcmeCertificateService acme,
        ReachabilityChecker reachability,
        PublicAddresses addresses,
        ActivityLogger? activity = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(addresses);
        _activity = activity;
        _reachability = reachability;
        _names = new NameChecker((name, token) => Dns.GetHostAddressesAsync(name, token), addresses.GetAsync);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ProxyManager>();
        _paths = paths;
        _network = network;
        _certificates = certificates;
        _challenges = challenges;
        _access = access;
        _blocks = blocks;
        _bans = bans;
        _localTraffic = new LocalTrafficWatch(ReportEveryoneLooksLocal);

        // One timer for the next Let's Encrypt try, armed and disarmed as the state changes.
        _acmeRetry = new Timer(_ => RetryAcme(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _geo = geo;
        _acme = acme;
        _systemId = application.SystemId;
        _certificates.AvailabilityChanged += OnCertificateAvailabilityChanged;
        _configuration = configuration;
        _jellyfin = JellyfinNetworkView.From(
            configuration.GetNetworkConfiguration(),
            application.HttpPort,
            application.ListenWithHttps ? application.HttpsPort : 0);
    }

    /// <summary>Gets the root devices install to trust a self-signed certificate, or <c>null</c> while no route uses one.</summary>
    public RootCertificate? SelfSignedRoot => ReadSettings().Routes.Any(r => r.Enabled && r.Certificate.Mode == CertificateMode.SelfSigned) ? _certificates.Root : null;

    /// <summary>Gets Jellyfin's running network setup.</summary>
    public JellyfinNetworkView Jellyfin => _jellyfin;

    /// <summary>Gets a value indicating whether the proxy is listening.</summary>
    public bool IsRunning => Volatile.Read(ref _host) is not null;

    /// <summary>Gets every route's certificate.</summary>
    public CertificateSet Certificates => _certificates;

    /// <summary>
    /// Reads the saved settings and brings the proxy in line: stops it when disabled, updates it in place when
    /// only live settings changed, rebuilds it when listeners changed. Never throws for a bind failure. The
    /// error is kept for the status page.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task.</returns>
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Once Jellyfin is shutting down, nothing may bind the ports again: the next instance needs them.
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        await ApplyLockedAsync(cancellationToken).ConfigureAwait(false);

        // Let's Encrypt checks the address through the running proxy, so a certificate is asked for only once
        // it listens. The planner makes this a no-op while the certificate is current or after a recent failure.
        if (IsRunning && ReadSettings() is { AcceptTerms: true } settings && AutomaticRoutes(settings).Count > 0)
        {
            _ = Task.Run(() => EnsureAutomaticCertificatesAsync(settings, _shutdown.Token), _shutdown.Token);
        }
    }

    /// <summary>
    /// Stops the proxy for good as Jellyfin shuts down: no certificate change, retry or settings save may start it
    /// again, and a certificate request still running is cancelled.
    /// </summary>
    /// <returns>A task.</returns>
    public async Task ShutdownAsync()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        _certificates.AvailabilityChanged -= OnCertificateAvailabilityChanged;
        await _acmeRetry.DisposeAsync().ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ApplyLockedAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            var settings = ReadSettings();
            if (!settings.Enabled || File.Exists(_paths.DisableFlag))
            {
                await StopHostAsync().ConfigureAwait(false);
                _error = settings.Enabled ? ServerText.Get(TranslationKeys.ProxyErrorRecoveryFlagPresent) : string.Empty;
                return;
            }

            var runtime = BuildRuntime(settings);

            // Country rules need the location database even when the visitor history does not.
            if (CountryRules.NeedsDatabase(runtime.Options))
            {
                _ = Task.Run(() => _geo.EnsureAsync(VisitorService.RequiredLocation, CancellationToken.None), CancellationToken.None);
            }

            // The proxy starts before Jellyfin's own server binds its ports. Taking one of them first would stop
            // Jellyfin from starting, so a clash is refused here rather than left to whichever binds first.
            var clash = JellyfinPortClash(runtime.Listener);
            if (clash.Count > 0)
            {
                await StopHostAsync().ConfigureAwait(false);
                _error = ServerText.Format(TranslationKeys.ProxyErrorJellyfinUsesPort, ServerText.List(clash.Cast<object>()));
                return;
            }

            if (_host is not null && _holder is not null && _host.Listener == runtime.Listener)
            {
                _holder.Current = runtime;
                _host.Update(runtime);
                return;
            }

            await StopHostAsync().ConfigureAwait(false);
            await StartHostAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Stops the proxy and releases its ports.
    /// </summary>
    /// <returns>A task.</returns>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopHostAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Asks a few places on the internet to connect to the first internet route's HTTPS port, and keeps the answer
    /// for the status. Takes up to about 20 seconds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait, not the check.</param>
    /// <returns>The result.</returns>
    public Task<ReachabilityResult> CheckReachabilityAsync(CancellationToken cancellationToken)
        => _reachability.CheckAsync(PublicHostname(ReadSettings()), cancellationToken);

    /// <summary>
    /// The name people use from the internet, the one the name and outside checks look at.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <returns>The first enabled internet route's exact public name, empty when there is none.</returns>
    public static string PublicHostname(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Routes
            .Where(r => r.Enabled && !r.LocalOnly)
            .Select(r => HostnameMatcher.Normalize(r.Host))
            .FirstOrDefault(h => h.Length > 0 && !h.StartsWith('*') && HostnameMatcher.IsPublicName(h)) ?? string.Empty;
    }

    /// <summary>
    /// The names of the enabled routes that reach this Jellyfin from the internet, for the address Jellyfin gives
    /// its clients and for the share card.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <returns>The normalized names, wildcards included.</returns>
    public static List<string> JellyfinHostnames(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Routes
            .Where(r => r.Enabled && r.IsJellyfin)
            .OrderBy(r => r.LocalOnly)
            .Select(r => HostnameMatcher.Normalize(r.Host))
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Starts a throwaway copy of the proxy on a free loopback port and fetches Jellyfin's public system
    /// info through it, proving the proxy can reach this Jellyfin before anything about Jellyfin changes.
    /// </summary>
    /// <param name="settings">The settings to test.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>Null when the probe passed, otherwise what went wrong.</returns>
    public async Task<string?> ProbeAsync(ProxySettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var runtime = ProxyRuntimeFactory.Build(settings, _jellyfin, IsLan, httpsAvailable: false, probe: true);
        ProxyHost? probe = null;
        try
        {
            probe = await ProxyHost.StartAsync(HostOptions(new ProxyRuntimeHolder(runtime)), cancellationToken).ConfigureAwait(false);
            var address = probe.Addresses.Count > 0 ? probe.Addresses[0] : throw new IOException("The probe did not report an address.");
            return await CheckJellyfinAsync(new Uri(address), runtime.BaseUrl, null, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return ServerText.Get(TranslationKeys.ProxyErrorTestListener) + " " + ex.Message;
        }
        finally
        {
            if (probe is not null)
            {
                await probe.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await probe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Fetches Jellyfin's public system info through the running proxy's HTTP listener.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Null when Jellyfin answered through the proxy, otherwise what went wrong.</returns>
    public async Task<string?> CheckRunningAsync(CancellationToken cancellationToken)
    {
        var host = Volatile.Read(ref _host);
        if (host is null)
        {
            return string.IsNullOrEmpty(_error) ? ServerText.Get(TranslationKeys.ProxyErrorProxyNotRunning) : _error;
        }

        var address = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), host.Listener.HttpPort).Uri;
        return await CheckJellyfinAsync(address, _jellyfin.BaseUrl, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one request through the running proxy for a route's name, as a local client would, and reports
    /// whether its service answered. A Jellyfin route must answer as this Jellyfin.
    /// </summary>
    /// <param name="routeId">The route to test.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>Null when the service answered, otherwise what went wrong.</returns>
    public async Task<string?> TestRouteAsync(string routeId, CancellationToken cancellationToken)
    {
        var host = Volatile.Read(ref _host);
        if (host is null)
        {
            return string.IsNullOrEmpty(_error) ? ServerText.Get(TranslationKeys.ProxyErrorProxyNotRunning) : _error;
        }

        var route = host is null ? null : _holder?.Current.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.Ordinal));
        if (route is null)
        {
            return ServerText.Get(TranslationKeys.ProxyErrorRouteNotActive);
        }

        // A wildcard is tried with a sample name under it, since the proxy routes by the name asked for.
        var name = route.Host.StartsWith("*.", StringComparison.Ordinal) ? "test" + route.Host[1..] : route.Host;
        var address = new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), host!.Listener.HttpPort).Uri;
        if (route.IsJellyfin)
        {
            return await CheckJellyfinAsync(address, _jellyfin.BaseUrl, name, cancellationToken).ConfigureAwait(false);
        }

        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Host = name;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            // A 502 or 504 is the proxy saying the service did not answer. Anything else is the service itself.
            return (int)response.StatusCode is 502 or 504
                ? ServerText.Format(TranslationKeys.ProxyErrorUpstreamUnreachable, route.Backend.Authority)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ServerText.Format(TranslationKeys.ProxyErrorUpstreamUnreachable, route.Backend.Authority) + " " + CredentialRedactor.Redact(ex.Message);
        }
    }

    /// <summary>
    /// Reloads the certificates from their files, replaces a self-signed one that is close to expiring, asks Let's
    /// Encrypt for the ones that are due, and brings the proxy in line. Run by the scheduled certificate task.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Each route's status afterwards.</returns>
    public async Task<List<RouteStatus>> RefreshCertificatesAsync(CancellationToken cancellationToken)
    {
        await ApplyLockedAsync(cancellationToken).ConfigureAwait(false);
        var settings = ReadSettings();
        if (IsRunning && settings.AcceptTerms)
        {
            await EnsureAutomaticCertificatesAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        _certificates.Refresh();
        return RouteStatuses(settings);
    }

    /// <summary>
    /// Orders a new Let's Encrypt certificate for a route now, past the usual timing rules, for an administrator who asked.
    /// </summary>
    /// <param name="routeId">The route.</param>
    /// <param name="cancellationToken">Cancels the order.</param>
    /// <returns>Whether a certificate was issued, and what to tell the administrator.</returns>
    public async Task<(bool Ok, string Message)> RequestCertificateAsync(string routeId, CancellationToken cancellationToken)
    {
        var settings = ReadSettings();
        var route = settings.Routes.FirstOrDefault(r => string.Equals(r.Id, routeId, StringComparison.Ordinal));
        if (route is not { Enabled: true } || route.Certificate.Mode != CertificateMode.Automatic)
        {
            return (false, ServerText.Get(TranslationKeys.ProxyErrorChooseLetsEncrypt));
        }

        if (!settings.AcceptTerms)
        {
            return (false, ServerText.Get(TranslationKeys.ProxyErrorAcceptTerms));
        }

        if (!IsRunning)
        {
            return (false, ServerText.Get(TranslationKeys.ProxyErrorProxyNotRunningAcme));
        }

        var records = ReadRecords();
        var request = RequestFor(route);
        var store = _certificates.For(route.Id);
        if (store is null)
        {
            return (false, ServerText.Get(TranslationKeys.ProxyErrorChooseLetsEncrypt));
        }

        if (await _acme.EnsureAsync(request, records, store.Status, cancellationToken, force: true).ConfigureAwait(false))
        {
            store.Refresh();
            return (true, ServerText.Get(TranslationKeys.ProxyMessageCertificateIssued));
        }

        var acme = _acme.Describe(request, records);
        if (!acme.Issuing && acme.RateLimited && acme.NextAttemptUtc is { } after && after > DateTime.UtcNow)
        {
            return (false, ServerText.Format(TranslationKeys.ProxyErrorRateLimited, after.ToString("u", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return (false, acme.Issuing
            ? ServerText.Get(TranslationKeys.ProxyErrorAlreadyRequesting)
            : acme.Names.Count == 0
                ? ServerText.Get(TranslationKeys.ProxyStepNeedsAddress)
                : acme.LastError.Length > 0 ? acme.LastError : ServerText.Get(TranslationKeys.ProxyStepLetsEncryptFailed));
    }

    /// <summary>
    /// Gives what Let's Encrypt is asked for a route: its folder, the names it can check, and the challenge.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The request.</returns>
    public AcmeRequest RequestFor(ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return new AcmeRequest(_paths.ForRoute(route.Id), CertificateHostnames(route), route.Certificate.Challenge, route.Certificate.DnsRecordId ?? string.Empty);
    }

    // Each route that wants one is asked for in turn. A new certificate on disk is loaded at once, and the set then
    // starts the HTTPS listener if it was off.
    private async Task EnsureAutomaticCertificatesAsync(ProxySettings settings, CancellationToken cancellationToken)
    {
        var records = ReadRecords();
        foreach (var route in AutomaticRoutes(settings))
        {
            try
            {
                var store = _certificates.For(route.Id);
                if (store is not null && await _acme.EnsureAsync(RequestFor(route), records, store.Status, cancellationToken).ConfigureAwait(false))
                {
                    store.Refresh();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Run in the background, so nothing else would ever see this.
                _logger.LogWarning(ex, "Getting a certificate from Let's Encrypt for {Host} failed", route.Host);
            }
        }

        ScheduleAcmeRetry(settings);
    }

    private static List<ProxyRoute> AutomaticRoutes(ProxySettings settings)
        => settings.Routes.Where(r => r.Enabled && r.Certificate.Mode == CertificateMode.Automatic && HostnameMatcher.Normalize(r.Host).Length > 0).ToList();

    // After a failure the planner waits out a backoff, and after a success the certificate is due for renewal at
    // a known time. Nothing else would ask at either time except the daily task, so a timer does.
    private void ScheduleAcmeRetry(ProxySettings settings)
    {
        // Shutdown has disposed the timer for good. One armed now would never be disposed.
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        var records = ReadRecords();
        TimeSpan? soonest = null;
        foreach (var route in AutomaticRoutes(settings))
        {
            var request = RequestFor(route);
            var retry = _acme.Describe(request, records).NextAttemptUtc;
            var next = retry ?? _acme.NextCheckUtc(request, records, _certificates.StatusOf(route.Id));
            if (next is not { } when)
            {
                continue;
            }

            // A check that is due at once after a run waits a few minutes, so a planner that keeps saying "now"
            // cannot turn the timer into a loop.
            var due = when - DateTime.UtcNow;
            var floor = retry is null ? TimeSpan.FromMinutes(5) : TimeSpan.Zero;
            due = due < floor ? floor : due + TimeSpan.FromSeconds(5);
            soonest = soonest is { } s && s < due ? s : due;
        }

        // At most a day ahead: a timer cannot wait past about 49 days, and the planner decides again when it fires.
        ArmAcmeRetry(soonest is { } d ? (d > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : d) : Timeout.InfiniteTimeSpan);
    }

    private void ArmAcmeRetry(TimeSpan due)
    {
        try
        {
            _acmeRetry.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed it between the check above and here. Nothing is left to retry.
            _logger.LogDebug("Not scheduling a Let's Encrypt retry during shutdown");
        }
    }

    private void RetryAcme()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        if (IsRunning && ReadSettings() is { AcceptTerms: true } settings)
        {
            _ = Task.Run(() => EnsureAutomaticCertificatesAsync(settings, _shutdown.Token), _shutdown.Token);
        }
    }

    // The names Let's Encrypt is asked for, leaving out names it can never check, such as nas.lan. An HTTP
    // challenge also needs a name the internet can reach, which a DNS challenge does not.
    private static List<string> CertificateHostnames(ProxyRoute route)
    {
        var host = HostnameMatcher.Normalize(route.Host);
        return host.Length > 0 && HostnameMatcher.IsPublicName(host) ? new List<string> { host } : new List<string>();
    }

    // The names a route's certificate is checked against. An automatic one only ever covers what Let's Encrypt can check.
    private static IReadOnlyList<string> CoveredHostnames(ProxyRoute route)
    {
        if (route.Certificate.Mode == CertificateMode.Automatic)
        {
            return CertificateHostnames(route);
        }

        var host = HostnameMatcher.Normalize(route.Host);
        return host.Length > 0 ? new List<string> { host } : new List<string>();
    }

    /// <summary>
    /// Describes every route's certificate.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <returns>One status per route, in the routes' order.</returns>
    public List<RouteStatus> RouteStatuses(ProxySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var records = ReadRecords();
        var active = Volatile.Read(ref _host) is null ? new HashSet<string>(StringComparer.Ordinal) : (_holder?.Current.Routes.Select(r => r.Id).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal));
        return settings.Routes.Select(route => new RouteStatus
        {
            Id = route.Id,
            Host = HostnameMatcher.Normalize(route.Host),
            Active = active.Contains(route.Id),
            Certificate = _certificates.StatusOf(route.Id),
            Acme = route.Certificate.Mode == CertificateMode.Automatic ? _acme.Describe(RequestFor(route), records) : new AcmeStatus()
        }).ToList();
    }

    /// <summary>
    /// Describes the proxy for the dashboard.
    /// </summary>
    /// <param name="takeover">The trust the proxy state.</param>
    /// <returns>The status.</returns>
    public ProxyStatus GetStatus(TakeoverState takeover) => GetStatus(takeover, null);

    /// <summary>
    /// Describes the proxy for the dashboard, with the public name looked up again when the last answer is more
    /// than a minute old.
    /// </summary>
    /// <param name="takeover">The trust the proxy state.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The status.</returns>
    public async Task<ProxyStatus> GetStatusAsync(TakeoverState takeover, CancellationToken cancellationToken)
    {
        var name = await _names.GetAsync(PublicHostname(ReadSettings()), cancellationToken).ConfigureAwait(false);
        return GetStatus(takeover, name);
    }

    private ProxyStatus GetStatus(TakeoverState takeover, NameStatus? name)
    {
        ArgumentNullException.ThrowIfNull(takeover);
        var host = Volatile.Read(ref _host);
        var settings = ReadSettings();
        var options = ProxyOptions.From(settings.Advanced);
        return new ProxyStatus
        {
            Enabled = settings.Enabled,
            Running = host is not null,
            StartedUtc = host is null ? null : _startedUtc,
            Addresses = host?.Addresses.ToList() ?? new List<string>(),
            Error = _error,
            Backend = _jellyfin.Backend.ToString(),
            JellyfinHttpPort = _jellyfin.HttpPort,
            JellyfinHttpsPort = _jellyfin.HttpsPort,
            JellyfinTrustsProxy = _jellyfin.TrustsForwarding,
            DisableFlagPresent = File.Exists(_paths.DisableFlag),
            DisableFlagPath = _paths.DisableFlag,
            Routes = RouteStatuses(settings),

            // An older rollback left Jellyfin unmanaged, which is all the page needs to know.
            TakeoverPhase = takeover.Phase == TakeoverPhase.Degraded ? TakeoverPhase.Unmanaged : takeover.Phase,
            TakeoverAppliedUtc = takeover.AppliedUtc,
            TakeoverError = takeover.LastError,
            DetectedLanAddress = NetworkTakeover.DetectLanAddress()?.ToString() ?? string.Empty,
            HttpPort = options.HttpPort,
            HttpsPort = options.HttpsPort,
            LanAddress = options.LanAddress,
            PublicUrl = PublicUrl(settings),
            CountryRefused = _blocks.Refused(),
            CountryDatabaseLoaded = _geo.IsLoaded,
            ServerCountry = _blocks.ServerCountry.Country,
            JellyfinExposedAt = ExposedAt(),
            FolderProblem = FolderAccess.Problem,
            EveryoneLooksLocal = _localTraffic.Suspected,
            CountryRule = DescribeCountryRule(options, _blocks.ServerCountry.Country),
            Name = name ?? _names.Cached(PublicHostname(settings)),
            Reachability = _reachability.Last,
        };
    }

    /// <summary>
    /// Says in plain words who the country rule lets in from the internet, since someone travelling abroad is the
    /// first to meet it.
    /// </summary>
    /// <param name="options">The resolved options.</param>
    /// <param name="serverCountry">This server's country, empty while unknown.</param>
    /// <returns>The sentence, empty when the rule is off.</returns>
    public static string DescribeCountryRule(ProxyOptions options, string serverCountry)
    {
        ArgumentNullException.ThrowIfNull(options);
        var others = options.Countries.Count;
        return options.CountryMode switch
        {
            CountryRuleMode.ServerCountry when serverCountry.Length > 0 => others switch
            {
                0 => ServerText.Format(TranslationKeys.ProxyCountryRuleServerOnly, serverCountry),
                1 => ServerText.Format(TranslationKeys.ProxyCountryRuleServerAndOne, serverCountry),
                _ => ServerText.Format(TranslationKeys.ProxyCountryRuleServerAndMany, serverCountry, others)
            },
            CountryRuleMode.ServerCountry => others switch
            {
                0 => ServerText.Get(TranslationKeys.ProxyCountryRuleServerUnknown),
                1 => ServerText.Get(TranslationKeys.ProxyCountryRuleServerUnknownAndOne),
                _ => ServerText.Format(TranslationKeys.ProxyCountryRuleServerUnknownAndMany, others)
            },
            CountryRuleMode.AllowOnly => others == 1
                ? ServerText.Get(TranslationKeys.ProxyCountryRuleAllowOne)
                : ServerText.Format(TranslationKeys.ProxyCountryRuleAllowMany, others),
            CountryRuleMode.Block => others == 1
                ? ServerText.Get(TranslationKeys.ProxyCountryRuleBlockOne)
                : ServerText.Format(TranslationKeys.ProxyCountryRuleBlockMany, others),
            _ => string.Empty
        };
    }

    // Trusting the proxy turns on Jellyfin's remote access, which also lets the internet reach Jellyfin's own
    // ports directly wherever they are reachable: a public address on this machine is the case it can see.
    private string ExposedAt()
    {
        var network = _configuration.GetNetworkConfiguration();
        if (!network.EnableRemoteAccess)
        {
            return string.Empty;
        }

        // Bound to specific addresses: exposed when one of them is public. Bound to every interface, or to none,
        // exposed when the machine has a public address at all.
        var bound = network.LocalNetworkAddresses
            .Select(a => IPAddress.TryParse(a, out var ip) ? ip : null)
            .OfType<IPAddress>()
            .ToList();
        if (bound.Count > 0 && !bound.Any(ip => ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)))
        {
            return bound.FirstOrDefault(ip => !ip.IsIPv6LinkLocal && IPAddressClassifier.IsPublic(ip))?.ToString() ?? string.Empty;
        }

        return NetworkTakeover.DetectPublicAddress()?.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _names.Dispose();
        _lifecycle.Dispose();
        _shutdown.Dispose();
    }

    private List<int> JellyfinPortClash(ListenerOptions listener)
    {
        var jellyfin = new[] { _jellyfin.HttpPort, _jellyfin.HttpsPort };
        return new[] { listener.HttpPort, listener.HttpsPort ?? 0 }
            .Where(p => p != 0 && jellyfin.Contains(p))
            .Distinct()
            .ToList();
    }

    // The internet always reaches HTTPS on port 443, so the address needs no port.
    private static string PublicUrl(ProxySettings settings)
    {
        var name = JellyfinHostnames(settings).FirstOrDefault(h => !h.StartsWith('*'));
        return name is null ? string.Empty : "https://" + name;
    }

    // Copies, so the certificate work never holds the configuration's own records.
    private static List<DNSRecord> ReadRecords()
        => Plugin.Instance?.ReadConfiguration(c => c.Ddns.Records.Select(r => r.Clone()).ToList()) ?? new List<DNSRecord>();

    private static ProxySettings ReadSettings()
    {
        var settings = Plugin.Instance?.ReadConfiguration(c => ProxyRuntimeFactory.Clone(c.Proxy)) ?? new ProxySettings();
        ProxySettingsValidator.Normalize(settings);
        return settings;
    }

    private ProxyRuntime BuildRuntime(ProxySettings settings)
    {
        _certificates.Configure(settings.Routes, CoveredHostnames);
        return ProxyRuntimeFactory.Build(settings, _jellyfin, IsLan, _certificates.HasCertificate);
    }

    // A certificate file appeared, or became unreadable: rebuild so the HTTPS listener follows it.
    private void OnCertificateAvailabilityChanged(object? sender, EventArgs e)
        => _ = Task.Run(async () =>
        {
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await ApplyAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Applying a changed certificate failed");
            }
        });

    private bool IsLan(IPAddress address) => _network.IsInLocalNetwork(address);

    private ProxyHostOptions HostOptions(ProxyRuntimeHolder holder) => new()
    {
        Holder = holder,
        Certificates = _certificates,
        Challenges = _challenges,
        LoggerFactory = _loggerFactory,
        AccessRecorder = _access,
        FailureTracker = _bans,
        LocalTraffic = _localTraffic,
        ConnectionFilters = new IConnectionFilter[] { _blocks },
        ContentRoot = EnsureDataDirectory()
    };

    private void ReportEveryoneLooksLocal()
    {
        _logger.LogWarning("Most visitors in the last hour came from one local address, so something in front of the proxy, such as rootless Docker, a tunnel or another proxy, may be hiding their addresses and bans, the block list and country rules cannot apply");
        _activity?.Log(
            ServerText.Get(TranslationKeys.ProxyActivityEveryoneLocal),
            "Necto.Proxy.EveryoneLooksLocal",
            ServerText.Format(TranslationKeys.ProxyActivityEveryoneLocalOverview, LocalTrafficWatch.Threshold),
            LogLevel.Warning);
    }

    private string EnsureDataDirectory()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        return _paths.DataDirectory;
    }

    // Caller holds _lifecycle.
    private async Task StartHostAsync(ProxyRuntime runtime, CancellationToken cancellationToken)
    {
        var holder = new ProxyRuntimeHolder(runtime);
        try
        {
            var host = await ProxyHost.StartAsync(HostOptions(holder), cancellationToken).ConfigureAwait(false);
            _holder = holder;
            _startedUtc = DateTime.UtcNow;
            _error = string.Empty;
            Volatile.Write(ref _host, host);
            var addresses = string.Join(", ", host.Addresses);
            _logger.LogInformation("Reverse proxy listening on {Addresses}", addresses);
            if (_reportedError.Length > 0)
            {
                _reportedError = string.Empty;
                _activity?.Log(ServerText.Get(TranslationKeys.ProxyActivityProxyStarted), "Necto.Proxy.Started", ServerText.Format(TranslationKeys.ProxyActivityProxyStartedOverview, addresses));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Any start failure is kept for the status page rather than thrown, so the startup check after it
            // still runs and reports it.
            _error = DescribeStartError(ex, runtime.Listener);
            _logger.LogError("Reverse proxy could not start: {Error}", _error);
            if (!string.Equals(_error, _reportedError, StringComparison.Ordinal))
            {
                _reportedError = _error;
                _activity?.Log(ServerText.Get(TranslationKeys.ProxyActivityProxyStartFailed), "Necto.Proxy.StartFailed", ActivityEntries.Plain(_error), LogLevel.Error);
            }
        }
    }

    // Caller holds _lifecycle.
    private async Task StopHostAsync()
    {
        var host = Interlocked.Exchange(ref _host, null);
        _holder = null;
        if (host is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(_stopTimeout);
        await host.StopAsync(timeout.Token).ConfigureAwait(false);
        await host.DisposeAsync().ConfigureAwait(false);
        _logger.LogInformation("Reverse proxy stopped");
    }

    private static string DescribeStartError(Exception ex, ListenerOptions listener)
    {
        var socket = FindSocketError(ex);
        var ports = listener.HttpsPort is { } https ? ServerText.List(new object[] { listener.HttpPort, https }) : listener.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (socket == SocketError.AccessDenied)
        {
            return OperatingSystem.IsLinux()
                ? ServerText.Format(TranslationKeys.ProxyErrorPortNotAllowed, ports) + " " + ServerText.Get(TranslationKeys.ProxyErrorPortNotAllowedLinux)
                : ServerText.Format(TranslationKeys.ProxyErrorPortNotAllowed, ports);
        }

        // Jellyfin's own ports are refused before binding, so a clash here is another program.
        if (socket == SocketError.AddressAlreadyInUse || ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
        {
            return ServerText.Format(TranslationKeys.ProxyErrorPortInUse, ports);
        }

        return CredentialRedactor.Redact(ex.Message);
    }

    private static SocketError? FindSocketError(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException socket)
            {
                return socket.SocketErrorCode;
            }
        }

        return null;
    }

    private async Task<string?> CheckJellyfinAsync(Uri proxyAddress, string baseUrl, string? hostName, CancellationToken cancellationToken)
    {
        // Ask for loopback explicitly: a probe listening on "localhost" may resolve either way.
        var target = new UriBuilder(proxyAddress) { Host = IPAddress.Loopback.ToString(), Path = baseUrl + "/System/Info/Public" }.Uri;
        using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            if (!string.IsNullOrEmpty(hostName))
            {
                request.Headers.Host = hostName;
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // With "Require HTTPS" on, Jellyfin sends this plain-HTTP check on to its own HTTPS port. That is still
            // Jellyfin answering through the proxy. Real visitors arrive over HTTPS and are not redirected.
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location?.Scheme == Uri.UriSchemeHttps)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return ServerText.Format(TranslationKeys.ProxyErrorJellyfinAnswered, (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);
            var id = json.RootElement.TryGetProperty("Id", out var value) ? value.GetString() : null;
            return SameId(id, _systemId) ? null : ServerText.Get(TranslationKeys.ProxyErrorOtherServerAnswered);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ServerText.Get(TranslationKeys.ProxyErrorJellyfinUnreachable) + " " + CredentialRedactor.Redact(ex.Message);
        }
    }

    /// <summary>
    /// Whether two Jellyfin server ids are the same, with or without dashes.
    /// </summary>
    /// <param name="a">One id.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when both are set and equal.</returns>
    internal static bool SameId(string? a, string? b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && string.Equals(a.Replace("-", string.Empty, StringComparison.Ordinal), b.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
}
