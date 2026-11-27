using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Tests that construct the plugin share its static instance, so they never run in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PluginInstanceCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "Plugin instance";
}

/// <summary>
/// Builds a real <see cref="DNSUpdateService"/> over a temp folder: a live plugin instance holding the
/// configuration, a real status store, stubbed detection and DNS lookups, and either a real DynDNS v2
/// provider answering from a stub HTTP responder or fake providers.
/// </summary>
internal sealed class OrchestratorHarness : IDisposable
{
    public const string DetectUrl = "https://ip.test/";

    private readonly string _dir;

    public OrchestratorHarness(params IDNSProvider[] extraProviders)
        : this(_ => extraProviders)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestratorHarness"/> class with extra providers.
    /// </summary>
    /// <param name="extraProviders">Builds providers added beside the DynDNS v2 one, given the harness's HTTP factory.</param>
    public OrchestratorHarness(Func<IHttpClientFactory, IDNSProvider[]> extraProviders)
    {
        _dir = Path.Join(Path.GetTempPath(), "necto-ddns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var paths = Stubs.Paths(_dir);
        var (xml, xmlStub) = InterfaceStub.Create<IXmlSerializer>();
        xmlStub.Answer("DeserializeFromFile", _ => new PluginConfiguration());
        Plugin = new Plugin(paths, xml);
        Plugin.MutateConfiguration(c =>
        {
            c.Ddns.Records = new List<DNSRecord>();
            c.Ddns.IPv4DetectionUrl = DetectUrl;
            c.Ddns.IPv6DetectionUrl = "https://ip6.test/";
            c.Ddns.SkipInternalAddresses = false;
            c.Ddns.BackoffAfterFailures = 3;
            c.Ddns.BackoffHours = 24;
            c.Ddns.ForceUpdateHours = 0;
            c.Ddns.RequestTimeoutSeconds = 5;
            c.Proxy = new ProxySettings();
            return false;
        });

        var http = Stubs.Http(() => new HttpClient(new HarnessHandler(this)));
        Status = new StatusStoreService(paths, NullLogger<StatusStoreService>.Instance);
        // The plugin's own key ring, as on a real server, so credentials are encrypted the way they are in use.
        Secrets = StableSecretProtection.CreateProtector(paths, DdnsServices.SecretsName, NullLogger.Instance);
        ActivityLog = Stubs.Activity(e => { lock (Activity) { Activity.Add(e); } });

        var providers = new List<IDNSProvider> { new DynDns2Provider(http, NullLogger<DynDns2Provider>.Instance) };
        providers.AddRange(extraProviders(http));
        Providers = providers;

        Service = new DNSUpdateService(
            new IPDetectionService(http, NullLogger<IPDetectionService>.Instance),
            new DNSLookupService(NullLogger<DNSLookupService>.Instance, ResolveAsync),
            Status,
            providers,
            Secrets,
            ActivityLog,
            NullLogger<DNSUpdateService>.Instance,
            Notifier);
    }

    public Plugin Plugin { get; }

    public StatusStoreService Status { get; }

    public SecretProtector Secrets { get; }

    public ActivityLogger ActivityLog { get; }

    public List<ActivityLog> Activity { get; } = new();

    public IReadOnlyList<IDNSProvider> Providers { get; }

    public FakeNotifier Notifier { get; } = new();

    public DNSUpdateService Service { get; }

    /// <summary>Gets or sets the public IPv4 the detection endpoint reports.</summary>
    public string PublicIPv4 { get; set; } = "198.51.100.10";

    /// <summary>Gets or sets the public IPv6 the detection endpoint reports.</summary>
    public string PublicIPv6 { get; set; } = "2001:db8::10";

    /// <summary>Gets or sets the DynDNS v2 server reply (status and body).</summary>
    public (HttpStatusCode Code, string Body) DdnsReply { get; set; } = (HttpStatusCode.OK, "good");

    /// <summary>Gets the number of requests the DynDNS v2 server received.</summary>
    public int DdnsCalls { get; private set; }

    /// <summary>Gets the names the DNS lookup was asked to resolve.</summary>
    public List<string> Lookups { get; } = new();

    /// <summary>Gets or sets what every DNS lookup answers; null makes every lookup fail.</summary>
    public IPAddress[]? LookupAnswer { get; set; }

    /// <summary>Builds a DynDNS v2 record pointed at the stub server.</summary>
    public static DNSRecord DynDnsRecord(string id = "rec1", string host = "home.example.com") => new()
    {
        Id = id,
        Name = id,
        Provider = DNSProviderKind.DynDns2,
        Hostname = host,
        Login = "user",
        Password = "pass",
        Server = "https://ddns.test",
        UpdateIPv4 = true,
        UpdateIPv6 = false
    };

    public void SetRecords(params DNSRecord[] records)
        => Plugin.MutateConfiguration(c =>
        {
            c.Ddns.Records = new List<DNSRecord>(records);
            return false;
        });

    public void SeedStatus(string id, RecordStatus status)
    {
        var data = Status.Load();
        data.Records[id] = status;
        Status.Save(data);
    }

    public RecordStatus StatusOf(string id)
        => Status.Load().Records.GetValueOrDefault(id) ?? throw new Xunit.Sdk.XunitException("no status for " + id);

    public Task<DNSUpdateOutcome> RunAsync(CancellationToken cancellationToken = default)
        => Service.RunAsync(cancellationToken, force: true);

    public Task<DNSUpdateOutcome> RunScheduledAsync(CancellationToken cancellationToken = default)
        => Service.RunAsync(cancellationToken);

    public void Dispose()
    {
        Service.Dispose();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // A file still held open is left for the system's temporary-file cleanup.
        }
    }

    /// <summary>
    /// Gets or sets an async hook consulted before the canned replies. Returning null falls through to them.
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage?>>? Hook { get; set; }

    internal (HttpStatusCode Code, string Body) Reply(HttpRequestMessage request)
    {
        var host = request.RequestUri!.Host;
        if (host == "ip.test")
        {
            return (HttpStatusCode.OK, PublicIPv4);
        }

        if (host == "ip6.test")
        {
            return (HttpStatusCode.OK, PublicIPv6);
        }

        if (host == "ddns.test")
        {
            DdnsCalls++;
            return DdnsReply;
        }

        if (host == "down.test")
        {
            // Nothing answers: the request never completes.
            throw new HttpRequestException("connection refused");
        }

        return (HttpStatusCode.NotFound, "unexpected host " + host);
    }

    private Task<IPAddress[]> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        lock (Lookups)
        {
            Lookups.Add(name);
        }

        return LookupAnswer is null
            ? Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound))
            : Task.FromResult(LookupAnswer);
    }
}

/// <summary>Answers the harness's HTTP requests: the async hook first, then the canned replies.</summary>
internal sealed class HarnessHandler : HttpMessageHandler
{
    private readonly OrchestratorHarness _harness;

    public HarnessHandler(OrchestratorHarness harness) => _harness = harness;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_harness.Hook is { } hook && await hook(request, cancellationToken).ConfigureAwait(false) is { } hooked)
        {
            return hooked;
        }

        var (code, body) = _harness.Reply(request);
        return new HttpResponseMessage(code) { Content = new StringContent(body) };
    }
}

/// <summary>
/// A provider whose behaviour each test scripts, for exercising the orchestrator around a push.
/// </summary>
internal sealed class FakeProvider : IDNSProvider
{
    private readonly Func<DNSRecord, DetectedIP, CancellationToken, Task<DNSUpdateResult>> _update;

    public FakeProvider(DNSProviderKind kind, Func<DNSRecord, DetectedIP, CancellationToken, Task<DNSUpdateResult>> update, bool supportsIPv6 = true)
    {
        Kind = kind;
        _update = update;
        SupportsIPv6 = supportsIPv6;
    }

    public DNSProviderKind Kind { get; }

    public string Label => Kind.ToString();

    public string Hint => string.Empty;

    public ProviderFields Fields => new();

    public bool SupportsIPv6 { get; }

    public List<DNSRecord> Received { get; } = new();

    public Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
    {
        lock (Received)
        {
            Received.Add(record);
        }

        return _update(record, ip, cancellationToken);
    }
}

/// <summary>
/// A provider built on <see cref="DNSProviderBase"/> that pushes each family through the shared per-family
/// loop with a scripted updater, so tests exercise the real per-family failure and timeout handling.
/// </summary>
internal sealed class PerFamilyProvider : DNSProviderBase
{
    private readonly Func<PerFamilyProvider, string, CancellationToken, Task<(bool Ok, string Message)>> _family;

    public PerFamilyProvider(IHttpClientFactory http, Func<PerFamilyProvider, string, CancellationToken, Task<(bool Ok, string Message)>> family)
        : base(http, NullLogger.Instance)
    {
        _family = family;
    }

    public override DNSProviderKind Kind => DNSProviderKind.Ns1;

    public override string Label => "per family";

    public override string Hint => string.Empty;

    public override ProviderFields Fields => new();

    /// <summary>Sends a GET through the base helper, so the attempt sees the response.</summary>
    public Task<HttpResult> GetAsync(string url, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, url, cancellationToken);

    public override Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
        => ApplyPerFamilyAsync(record, ip, (type, _, ct) => _family(this, type, ct), cancellationToken);
}
