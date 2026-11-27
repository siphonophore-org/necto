using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Proxy;

/// <summary>
/// Routes: the templates, which route answers a name, who may reach a route, where each forwards, the per route
/// certificate files and their pick at the handshake, and how a route's certificate is proved.
/// </summary>
public sealed class RoutesTests : IDisposable
{
    private static readonly JellyfinNetworkView Jellyfin = new(new Uri("http://127.0.0.1:8096/"), string.Empty, true, 8096, 0);
    private readonly string _dir = Path.Join(Path.GetTempPath(), "necto-routes-" + Guid.NewGuid().ToString("N"));

    public RoutesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
            // Left for the system's temporary-file cleanup.
        }
    }

    private static ProxySettings Settings(params ProxyRoute[] routes)
    {
        var s = new ProxySettings { Enabled = true, Routes = routes.ToList() };
        ProxySettingsValidator.Normalize(s);
        return s;
    }

    private static ProxyRuntime Runtime(ProxySettings settings, bool trusts = true)
        => ProxyRuntimeFactory.Build(settings, Jellyfin with { TrustsForwarding = trusts }, _ => false, httpsAvailable: true);

    private static RequestDecision Decide(ProxyRuntime runtime, bool local, string host, string path = "/")
        => RequestPolicy.Decide(runtime, new RequestFacts(local, true, host, "GET", path, QueryString.Empty), new Http01ChallengeStore());

    /// <summary>
    /// The Jellyfin template opens the domain to the internet with home under it as its local address, and Blank
    /// forwards to another service.
    /// True: each template starts the route the Plan describes.
    /// False: the LAN route would ask for an HTTP challenge Let's Encrypt can never reach.
    /// </summary>
    [Fact]
    public void Templates_BuildTheRoutesThePlanDescribes()
    {
        var jellyfin = RouteTemplates.Build(RouteTemplates.Jellyfin, "Example.com.")!;
        Assert.Equal("example.com", jellyfin.Host);
        Assert.True(jellyfin.IsJellyfin);
        Assert.False(jellyfin.LocalOnly);
        Assert.Equal(CertificateMode.Automatic, jellyfin.Certificate.Mode);
        Assert.Equal("home.example.com", jellyfin.LocalHost);
        Assert.Equal(string.Empty, RouteTemplates.Build(RouteTemplates.Jellyfin, string.Empty)!.LocalHost);

        var blank = RouteTemplates.Build(RouteTemplates.Blank, "example.com")!;
        Assert.False(blank.IsJellyfin);
        Assert.Equal(string.Empty, blank.Host);
        Assert.Null(RouteTemplates.Build("other", "example.com"));
    }

    /// <summary>
    /// The suggested domain is the first enabled DDNS record's zone, or its name.
    /// True: the templates start under the domain the records keep.
    /// False: every new route would start with an empty name.
    /// </summary>
    [Fact]
    public void Templates_SuggestTheDomainFromTheDdnsRecords()
    {
        Assert.Equal("example.com", RouteTemplates.SuggestDomain(new[] { new DNSRecord { Enabled = false, Zone = "off.test" }, new DNSRecord { Enabled = true, Zone = "Example.com" } }));
        Assert.Equal("home.example.org", RouteTemplates.SuggestDomain(new[] { new DNSRecord { Provider = DNSProviderKind.DynDns2, Hostname = "home.example.org" } }));
        Assert.Equal(string.Empty, RouteTemplates.SuggestDomain(Array.Empty<DNSRecord>()));
    }

    /// <summary>
    /// An exact name beats a wildcard, a turned off route answers nothing, and a route to another service forwards
    /// to its own upstream.
    /// True: each name reaches the service its route names.
    /// False: a wildcard would swallow names that have their own route.
    /// </summary>
    [Fact]
    public void Runtime_PicksTheRouteForEachName()
    {
        var runtime = Runtime(Settings(
            new ProxyRoute { Id = "w", Host = "*.example.com", Upstream = "http://192.168.1.30:3000" },
            new ProxyRoute { Id = "m", Host = "media.example.com" },
            new ProxyRoute { Id = "off", Host = "off.example.com", Enabled = false }));

        Assert.Equal("m", runtime.RouteFor("media.example.com")!.Id);
        Assert.Equal("w", runtime.RouteFor("app.example.com")!.Id);
        Assert.Equal(new Uri("http://192.168.1.30:3000/"), runtime.RouteFor("app.example.com")!.Backend);
        Assert.Equal(Jellyfin.Backend, runtime.RouteFor("media.example.com")!.Backend);
        Assert.Equal("w", runtime.RouteFor("off.example.com")!.Id);
        Assert.Null(runtime.RouteFor("example.org"));
        Assert.Equal("m", runtime.RouteForLocal("192.168.1.10")!.Id);
    }

    /// <summary>
    /// A local only route refuses the internet and answers the local network.
    /// True: Jellyfin LAN stays inside the house.
    /// False: a name meant for home would answer the internet.
    /// </summary>
    [Fact]
    public void Policy_LocalOnlyRoute_RefusesTheInternet()
    {
        var runtime = Runtime(Settings(new ProxyRoute { Id = "h", Host = "home.example.com", LocalOnly = true }));
        Assert.Equal(RequestAction.Drop, Decide(runtime, false, "home.example.com").Action);
        Assert.Equal(RequestAction.Forward, Decide(runtime, true, "home.example.com").Action);
        Assert.Equal(RequestAction.Drop, RequestPolicy.Decide(runtime, new RequestFacts(false, false, "home.example.com", "GET", "/", QueryString.Empty), new Http01ChallengeStore()).Action);
    }

    /// <summary>
    /// The Jellyfin side checks apply only to routes to this Jellyfin: another service is reached before Jellyfin
    /// trusts the proxy, and its paths are never read as Jellyfin's.
    /// True: the proxy fronts any service, not only Jellyfin.
    /// False: an app's own /Startup page would be refused, or it would wait on Jellyfin's trust.
    /// </summary>
    [Fact]
    public void Policy_JellyfinChecks_ApplyOnlyToJellyfinRoutes()
    {
        var runtime = Runtime(Settings(
            new ProxyRoute { Id = "m", Host = "media.example.com" },
            new ProxyRoute { Id = "a", Host = "app.example.com", Upstream = "http://192.168.1.30:3000" }), trusts: false);

        Assert.Equal(RequestAction.Drop, Decide(runtime, false, "media.example.com").Action);
        var app = Decide(runtime, false, "app.example.com", "/Startup/Configuration");
        Assert.Equal(RequestAction.Forward, app.Action);
        Assert.Equal("a", app.Route!.Id);

        var trusted = Runtime(Settings(new ProxyRoute { Id = "m", Host = "media.example.com" }));
        Assert.Equal(RequestAction.Forbidden, Decide(trusted, false, "media.example.com", "/Startup/Configuration").Action);
    }

    /// <summary>
    /// YARP gets a route and a cluster per route, and a media pair for each Jellyfin route only.
    /// True: each route forwards to its own upstream, and Jellyfin streams keep their long timer.
    /// False: every route would forward to Jellyfin.
    /// </summary>
    [Fact]
    public void ConfigProvider_BuildsARouteAndClusterPerRoute()
    {
        var runtime = Runtime(Settings(
            new ProxyRoute { Id = "m", Host = "media.example.com" },
            new ProxyRoute { Id = "a", Host = "app.example.com", Upstream = "https://192.168.1.30:8443" }));

        Assert.Equal(new[] { "m", "m-media", "a" }, ProxyConfigProvider.BuildRoutes(runtime).Select(r => r.RouteId).ToArray());
        var clusters = ProxyConfigProvider.BuildClusters(runtime);
        Assert.Equal("https://192.168.1.30:8443/", clusters.Single(c => c.ClusterId == "a").Destinations!.Single().Value.Address);
        Assert.Equal(ProxyConfigProvider.MediaActivityTimeout, clusters.Single(c => c.ClusterId == "m-media").HttpRequest!.ActivityTimeout);
        Assert.Equal(256, clusters.Single(c => c.ClusterId == "m").HttpClient!.MaxConnectionsPerServer);
    }

    /// <summary>
    /// A route to Jellyfin forwards with its profile: Low memory opens fewer connections and lets idle streams go
    /// sooner, and its upload limit reaches the policy.
    /// True: a small server holds what the profile says.
    /// False: the profile would change nothing the proxy does.
    /// </summary>
    [Fact]
    public void ConfigProvider_JellyfinRoute_FollowsItsProfile()
    {
        var runtime = Runtime(Settings(new ProxyRoute { Id = "m", Host = "media.example.com", Options = new RouteOptions { Profile = JellyfinProfile.LowMemory } }));

        var clusters = ProxyConfigProvider.BuildClusters(runtime);
        Assert.Equal(TimeSpan.FromMinutes(60), clusters.Single(c => c.ClusterId == "m-media").HttpRequest!.ActivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), clusters.Single(c => c.ClusterId == "m").HttpRequest!.ActivityTimeout);
        Assert.All(clusters, c => Assert.Equal(32, c.HttpClient!.MaxConnectionsPerServer));
        Assert.Equal(20, runtime.Routes.Single().Options!.MaxBodyMb);
        Assert.False(clusters.Single(c => c.ClusterId == "m").HttpRequest!.AllowResponseBuffering);
    }

    /// <summary>
    /// A route's folder is named by its id, and an id that could reach outside the folder is refused.
    /// True: certificate files never land outside the proxy's folder.
    /// False: a crafted id could write a key anywhere Jellyfin can.
    /// </summary>
    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Paths_ForRoute_RefusesAnIdThatLeavesTheFolder(string id)
        => Assert.Throws<ArgumentException>(() => new ProxyPaths(_dir).ForRoute(id));

    /// <summary>
    /// Two routes keep their certificates apart, and a handshake gets the one whose name it asked for. A local client
    /// that names no route gets the Jellyfin route's.
    /// True: each name is served its own certificate.
    /// False: a visitor to one name would get another's certificate.
    /// </summary>
    [Fact]
    public void CertificateSet_PicksTheCertificateByName()
    {
        var paths = new ProxyPaths(_dir);
        WriteIssued(paths.ForRoute("m"), "media.example.com");
        WriteIssued(paths.ForRoute("a"), "app.example.com");
        using var set = new CertificateSet(paths, new SecretProtector("test", NullLogger.Instance), NullLoggerFactory.Instance);
        var routes = new List<ProxyRoute>
        {
            new() { Id = "m", Host = "media.example.com" },
            new() { Id = "a", Host = "app.example.com", Upstream = "http://192.168.1.30:3000" }
        };
        set.Configure(routes, r => new[] { r.Host });

        Assert.True(set.HasCertificate);
        Assert.Equal(new[] { "media.example.com" }, set.StatusOf("m").Names);
        Assert.Equal(new[] { "app.example.com" }, set.StatusOf("a").Names);
        Assert.NotNull(set.Select("app.example.com", false));
        Assert.Null(set.Select("other.example.com", false));
        Assert.Same(set.Select("media.example.com", false), set.Select(null, true));

        set.Configure(routes.Take(1), r => new[] { r.Host });
        Assert.Null(set.For("a"));
        Assert.Equal(CertificateMode.None, set.StatusOf("a").Mode);
    }

    /// <summary>
    /// A route that asks for HTTP is proved over HTTP, one that asks for DNS uses the record it names even without a
    /// zone, and one on Auto uses DNS only when a record covers it.
    /// True: the LAN template gets its DNS challenge through the DDNS provider's credentials.
    /// False: a local name would be sent to an HTTP check that can never reach it.
    /// </summary>
    [Fact]
    public void AcmePlan_FollowsTheRoutesChallenge()
    {
        var cloudflare = new DNSRecord { Id = "cf", Enabled = true, Provider = DNSProviderKind.Cloudflare, Zone = "example.com" };
        var other = new DNSRecord { Id = "o", Enabled = true, Provider = DNSProviderKind.Cloudflare };
        Func<DNSProviderKind, bool> txt = k => k == DNSProviderKind.Cloudflare;
        var names = new[] { "home.example.com" };

        Assert.Equal(AcmeMethod.Http, AcmePlan.For(names, new[] { cloudflare }, txt, CertificateChallenge.Http).Method);
        Assert.Equal(AcmeMethod.Dns, AcmePlan.For(names, new[] { cloudflare }, txt, CertificateChallenge.Auto).Method);
        Assert.Equal(AcmeMethod.Http, AcmePlan.For(names, Array.Empty<DNSRecord>(), txt, CertificateChallenge.Auto).Method);

        var named = AcmePlan.For(names, new[] { cloudflare, other }, txt, CertificateChallenge.Dns, "o");
        Assert.Equal(AcmeMethod.Dns, named.Method);
        Assert.Equal("o", named.Records["home.example.com"].Id);

        var missing = AcmePlan.For(names, Array.Empty<DNSRecord>(), txt, CertificateChallenge.Dns);
        Assert.Equal(AcmeMethod.Dns, missing.Method);
        Assert.Empty(missing.Records);
    }

    /// <summary>
    /// An uploaded certificate loads as a .pfx with its password or as a PEM chain with its key, and anything
    /// without a private key is refused.
    /// True: only a certificate the proxy can serve is kept.
    /// False: a chain without its key would be saved and fail every handshake.
    /// </summary>
    [Fact]
    public void Upload_NeedsACertificateWithItsKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=media.example.com", key, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
        var pfx = cert.Export(X509ContentType.Pkcs12, "secret");
        var chain = System.Text.Encoding.UTF8.GetBytes(cert.ExportCertificatePem());
        var keyPem = System.Text.Encoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem());

        Assert.Null(ProxyController.UploadProblem(pfx, null, "secret", pem: false));
        Assert.NotNull(ProxyController.UploadProblem(pfx, null, "wrong", pem: false));
        Assert.Null(ProxyController.UploadProblem(chain, keyPem, string.Empty, pem: true));
        Assert.NotNull(ProxyController.UploadProblem(chain, null, string.Empty, pem: true));
    }

    /// <summary>
    /// The names Jellyfin is published under come from the enabled Jellyfin routes, internet ones first.
    /// True: Jellyfin hands out the public address its visitors use.
    /// False: a route to another service would become Jellyfin's address.
    /// </summary>
    [Fact]
    public void JellyfinHostnames_AreTheJellyfinRoutesInternetFirst()
    {
        var settings = Settings(
            new ProxyRoute { Id = "h", Host = "home.example.com", LocalOnly = true },
            new ProxyRoute { Id = "a", Host = "app.example.com", Upstream = "http://192.168.1.30:3000" },
            new ProxyRoute { Id = "m", Host = "media.example.com" });

        Assert.Equal(new[] { "media.example.com", "home.example.com" }, ProxyManager.JellyfinHostnames(settings));
        Assert.Equal("media.example.com", ProxyManager.PublicHostname(settings));
    }

    /// <summary>
    /// A route to Jellyfin answers its remote address from anywhere and its local address only from the local network.
    /// True: kelp.ink reaches Jellyfin from the internet and home.kelp.ink only from home, as one route.
    /// False: the local address would be open to the internet, or would need a route of its own.
    /// </summary>
    [Fact]
    public void Policy_LocalAddress_AnswersOnlyTheLocalNetwork()
    {
        var runtime = Runtime(Settings(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "Home.Kelp.Ink" }));

        Assert.Equal("home.kelp.ink", Assert.Single(runtime.Routes).LocalHost);
        Assert.Equal(RequestAction.Forward, Decide(runtime, false, "kelp.ink").Action);
        Assert.Equal(RequestAction.Forward, Decide(runtime, true, "home.kelp.ink").Action);
        Assert.Equal(RequestAction.Drop, Decide(runtime, false, "home.kelp.ink").Action);
        Assert.Equal("j", Decide(runtime, true, "home.kelp.ink").Route!.Id);
        var http = RequestPolicy.Decide(runtime, new RequestFacts(false, false, "home.kelp.ink", "GET", "/", QueryString.Empty), new Http01ChallengeStore());
        Assert.Equal(RequestAction.Drop, http.Action);
    }

    /// <summary>
    /// A local address must be one name of its own, never the route's host, a wildcard, or another route's name, and
    /// it cannot be proved over HTTP.
    /// True: the editor refuses a local address that could never work.
    /// False: two routes could answer one name, or Let's Encrypt would be sent to a name it cannot reach.
    /// </summary>
    [Fact]
    public void Validate_LocalAddress_IsOneNameOfItsOwn()
    {
        var other = new ProxyRoute { Id = "o", Host = "app.kelp.ink", Upstream = "http://h:1" };
        static List<string> Errors(ProxyRoute route, params ProxyRoute[] others)
        {
            ProxySettingsValidator.NormalizeRoute(route);
            return ProxySettingsValidator.ValidateRoute(route, others);
        }

        Assert.Empty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "home.kelp.ink" }, other));
        Assert.NotEmpty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "kelp.ink" }));
        Assert.NotEmpty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "*.kelp.ink" }));
        Assert.NotEmpty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "app.kelp.ink" }, other));
        Assert.NotEmpty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "home.kelp.ink", Certificate = new ProxyCertificateSettings { Challenge = CertificateChallenge.Http } }));
        Assert.NotEmpty(Errors(new ProxyRoute { Id = "j", Host = "kelp.ink", Certificate = new ProxyCertificateSettings { Mode = CertificateMode.SelfSigned, Wildcard = true } }));

        var service = new ProxyRoute { Id = "s", Host = "app.kelp.ink", Upstream = "http://h:1", LocalHost = "home.kelp.ink" };
        ProxySettingsValidator.NormalizeRoute(service);
        Assert.Empty(service.LocalHost);
    }

    /// <summary>
    /// A stored local only route to Jellyfin folds into the internet one as its local address, keeping the DNS
    /// challenge it was proved with, and nothing changes without exactly one internet route to Jellyfin.
    /// True: an upgrade turns the two Jellyfin routes into one route with a remote and a local address.
    /// False: the local name would be lost, or a route to another service would be folded in.
    /// </summary>
    [Fact]
    public void Normalize_FoldsTheLocalJellyfinRouteIntoTheRemoteOne()
    {
        var settings = new ProxySettings
        {
            Routes =
            {
                new ProxyRoute { Id = "m", Host = "kelp.ink" },
                new ProxyRoute { Id = "h", Host = "home.kelp.ink", LocalOnly = true, Certificate = new ProxyCertificateSettings { Challenge = CertificateChallenge.Dns, DnsRecordId = "r1" } },
                new ProxyRoute { Id = "a", Host = "app.kelp.ink", Upstream = "http://h:1", LocalOnly = true }
            }
        };

        ProxySettingsValidator.Normalize(settings);

        Assert.Equal(new[] { "m", "a" }, settings.Routes.Select(r => r.Id));
        Assert.Equal("home.kelp.ink", settings.Routes[0].LocalHost);
        Assert.Equal(CertificateChallenge.Dns, settings.Routes[0].Certificate.Challenge);
        Assert.Equal("r1", settings.Routes[0].Certificate.DnsRecordId);
        Assert.False(ProxySettingsValidator.MergeJellyfinRoutes(settings));

        var onlyLocal = new ProxySettings { Routes = { new ProxyRoute { Id = "h", Host = "home.kelp.ink", LocalOnly = true } } };
        Assert.False(ProxySettingsValidator.MergeJellyfinRoutes(onlyLocal));
        Assert.Single(onlyLocal.Routes);
    }

    /// <summary>
    /// A wildcard certificate asks for the domain and every name under it, adds a local address it does not cover,
    /// and every other route under the domain shares it.
    /// True: one certificate for kelp.ink covers kelp.ink, home.kelp.ink, and every route under kelp.ink.
    /// False: each route would ask Let's Encrypt for its own, or a route outside the domain would use the wildcard.
    /// </summary>
    [Fact]
    public void RouteCertificates_WildcardCoversTheDomainAndIsShared()
    {
        var jellyfin = new ProxyRoute { Id = "j", Host = "kelp.ink", LocalHost = "home.kelp.ink", Certificate = new ProxyCertificateSettings { Wildcard = true, Challenge = CertificateChallenge.Dns } };
        var app = new ProxyRoute { Id = "a", Host = "app.kelp.ink", Upstream = "http://h:1" };
        var deep = new ProxyRoute { Id = "d", Host = "a.b.kelp.ink", Upstream = "http://h:1" };
        var elsewhere = new ProxyRoute { Id = "e", Host = "media.example.com", Upstream = "http://h:1" };
        var routes = new[] { jellyfin, app, deep, elsewhere };

        Assert.Equal(new[] { "kelp.ink", "*.kelp.ink" }, RouteCertificates.Names(jellyfin));
        Assert.Equal(new[] { "kelp.ink" }, RouteCertificates.Names(new ProxyRoute { Host = "kelp.ink", LocalHost = "home.lan" }));
        Assert.Equal(new[] { "kelp.ink", "home.kelp.ink" }, RouteCertificates.Names(new ProxyRoute { Host = "kelp.ink", LocalHost = "home.kelp.ink" }));
        Assert.Same(jellyfin, RouteCertificates.CoveredBy(routes, app));
        Assert.Null(RouteCertificates.CoveredBy(routes, deep));
        Assert.Null(RouteCertificates.CoveredBy(routes, elsewhere));
        Assert.Null(RouteCertificates.CoveredBy(routes, jellyfin));
        Assert.Equal("kelp.ink", RouteCertificates.WildcardDomain(new ProxyRoute { Host = "*.kelp.ink" }));

        var plan = AcmePlan.For(RouteCertificates.Names(jellyfin), new[] { new DNSRecord { Id = "r", Provider = DNSProviderKind.Porkbun, Hostname = "kelp.ink", Zone = "kelp.ink" } }, kind => kind == DNSProviderKind.Porkbun, CertificateChallenge.Dns);
        Assert.Equal(AcmeMethod.Dns, plan.Method);
        Assert.Equal(new[] { "*.kelp.ink", "kelp.ink" }, plan.Names);
        Assert.Equal("_acme-challenge.kelp.ink", AcmePlan.TxtName(plan.Names[0]));
        Assert.Equal("_acme-challenge.kelp.ink", AcmePlan.TxtName(plan.Names[1]));
    }

    /// <summary>
    /// A handshake for a name under a wildcard certificate gets it, though the name belongs to another route.
    /// True: app.kelp.ink is served the kelp.ink wildcard, which covers it.
    /// False: every route under the domain would need its own certificate.
    /// </summary>
    [Fact]
    public void CertificateSet_ServesTheWildcardForNamesUnderIt()
    {
        var paths = new ProxyPaths(_dir);
        WriteIssued(paths.ForRoute("j"), "*.kelp.ink");
        using var set = new CertificateSet(paths, new SecretProtector("test", NullLogger.Instance), NullLoggerFactory.Instance);
        set.Configure(new[] { new ProxyRoute { Id = "j", Host = "kelp.ink", Certificate = new ProxyCertificateSettings { Wildcard = true, Mode = CertificateMode.Automatic } } }, r => new[] { "kelp.ink", "*.kelp.ink" });

        Assert.NotNull(set.Select("app.kelp.ink", false));
        Assert.Null(set.Select("media.example.com", false));
    }

    private static void WriteIssued(CertificatePaths slot, string name)
    {
        Directory.CreateDirectory(slot.Directory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(name);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));
        File.WriteAllText(slot.AcmeChainFile, cert.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem());
    }
}
