using System.Collections.Generic;
using System.Linq;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Proxy;

/// <summary>
/// Normalization and validation of proxy settings, and how unset advanced values resolve to defaults.
/// </summary>
public class ProxySettingsTests
{
    private static List<string> Errors(ProxySettings s)
    {
        ProxySettingsValidator.Normalize(s);
        return ProxySettingsValidator.Validate(s);
    }

    private static List<string> RouteErrors(ProxyRoute route, params ProxyRoute[] others)
    {
        ProxySettingsValidator.NormalizeRoute(route);
        return ProxySettingsValidator.ValidateRoute(route, others);
    }

    [Fact]
    public void Defaults_AreValid()
        => Assert.Empty(Errors(new ProxySettings()));

    [Fact]
    public void UnsetValues_FollowTheDefaults()
    {
        var o = ProxyOptions.From(new ProxyAdvancedSettings());
        // Lists compare by reference in a record, so they are checked separately.
        Assert.Equal(ProxyOptions.Defaults, o with { TrustedNetworks = ProxyOptions.Defaults.TrustedNetworks, BlockedAddresses = ProxyOptions.Defaults.BlockedAddresses, Countries = ProxyOptions.Defaults.Countries });
        Assert.Empty(o.TrustedNetworks);
        Assert.Empty(o.Countries);
        Assert.Equal(80, o.HttpPort);
        Assert.Equal(443, o.HttpsPort);
    }

    [Fact]
    public void LowMemory_HoldsFewerConnections_AndKeepsTheRest()
    {
        var low = ProxyOptions.From(new ProxyAdvancedSettings(), ProxyOptions.LowMemory);
        Assert.True(low.MaxConnections < ProxyOptions.Standard.MaxConnections);
        Assert.True(low.MaxConnectionsPerIp < ProxyOptions.Standard.MaxConnectionsPerIp);
        Assert.True(low.KeepAliveSeconds < ProxyOptions.Standard.KeepAliveSeconds);
        Assert.Equal(ProxyOptions.Standard.MaxRequestBodyMbWan, low.MaxRequestBodyMbWan);

        // A value the administrator chose wins over either set.
        Assert.Equal(1000, ProxyOptions.From(new ProxyAdvancedSettings { MaxConnections = 1000 }, ProxyOptions.LowMemory).MaxConnections);
        Assert.Empty(Errors(new ProxySettings()));
    }

    [Fact]
    public void ChangedValues_Win()
    {
        var o = ProxyOptions.From(new ProxyAdvancedSettings { HttpsPort = 9443, KeepAliveSeconds = 60, HstsEnabled = false });
        Assert.Equal(9443, o.HttpsPort);
        Assert.Equal(60, o.KeepAliveSeconds);
        Assert.False(o.HstsEnabled);
        Assert.Equal(ProxyOptions.Defaults.RequestHeadersTimeoutSeconds, o.RequestHeadersTimeoutSeconds);
    }

    [Fact]
    public void Normalize_KeepsUnsetValuesUnset()
    {
        var s = new ProxySettings();
        ProxySettingsValidator.Normalize(s);
        Assert.Null(s.Advanced.HttpsPort);
        Assert.Null(s.Advanced.KeepAliveSeconds);
        Assert.Null(s.Advanced.HstsEnabled);
    }

    [Fact]
    public void Validate_PortClashes()
    {
        Assert.NotEmpty(Errors(new ProxySettings { Advanced = new ProxyAdvancedSettings { HttpPort = 8920, HttpsPort = 8920 } }));
        Assert.NotEmpty(Errors(new ProxySettings { Advanced = new ProxyAdvancedSettings { HttpsPort = 70000 } }));
    }

    [Fact]
    public void Validate_TrustedNetworks()
    {
        var s = new ProxySettings();
        s.Advanced.TrustedNetworks = new List<string> { "100.64.0.0/10", "10.1.2.3", "nope" };
        Assert.Single(Errors(s));
    }

    /// <summary>
    /// A route's host is lower-cased and trimmed, and advanced values are clamped.
    /// True: names compare as the proxy matches them.
    /// False: a route saved as Media.Example.com would never match a request.
    /// </summary>
    [Fact]
    public void Normalize_CleansHostnamesAndClamps()
    {
        var s = new ProxySettings { Routes = TestRoutes.Of(" Media.Example.com ") };
        s.Advanced.BackendActivityTimeoutSeconds = 10;
        s.Advanced.MaxConnections = -5;
        ProxySettingsValidator.Normalize(s);

        Assert.Equal(new[] { "media.example.com" }, TestRoutes.Hosts(s));
        Assert.Equal(61, s.Advanced.BackendActivityTimeoutSeconds);
        Assert.Equal(0, s.Advanced.MaxConnections);
    }

    /// <summary>
    /// Missing sections are filled in, and a route without a usable id gets one.
    /// True: a save from an older page still has every section.
    /// False: a null section would throw on the next read.
    /// </summary>
    [Fact]
    public void Normalize_FillsMissingSections()
    {
        var s = new ProxySettings { Advanced = null!, Visitors = null!, Routes = new List<ProxyRoute> { new() { Id = "../x", Host = "a.example.com", Certificate = null! }, null! } };
        ProxySettingsValidator.Normalize(s);
        Assert.NotNull(s.Advanced);
        Assert.NotNull(s.Visitors);
        var route = Assert.Single(s.Routes);
        Assert.NotNull(route.Certificate);
        Assert.Matches("^[a-z0-9]{32}$", route.Id);
    }

    /// <summary>
    /// A route with a host that is not a name, no host, or the host another route has is refused.
    /// True: every route answers for one name only it has.
    /// False: two routes would fight over one name.
    /// </summary>
    [Fact]
    public void ValidateRoute_HostMustBeANameNoOtherRouteHas()
    {
        Assert.Contains(RouteErrors(new ProxyRoute { Host = "not a host" }), e => e.Contains("not a host", System.StringComparison.Ordinal));
        Assert.Contains(RouteErrors(new ProxyRoute { Host = string.Empty }), e => e == "Enter the host name.");
        Assert.Contains(RouteErrors(new ProxyRoute { Id = "b", Host = "media.example.com" }, new ProxyRoute { Id = "a", Host = "media.example.com" }), e => e.Contains("already answers", System.StringComparison.Ordinal));
        Assert.Empty(RouteErrors(new ProxyRoute { Id = "a", Host = "media.example.com" }, new ProxyRoute { Id = "a", Host = "media.example.com" }));
    }

    /// <summary>
    /// A route to another service needs an http or https address with nothing after the port.
    /// True: the proxy forwards each path as it came.
    /// False: a typo would send visitors nowhere.
    /// </summary>
    [Theory]
    [InlineData("http://192.168.1.20:8096", true)]
    [InlineData("https://nas.lan", true)]
    [InlineData("ftp://nas.lan", false)]
    [InlineData("http://user:pass@nas.lan", false)]
    [InlineData("http://nas.lan/app", false)]
    [InlineData("http://", false)]
    public void ValidateRoute_UpstreamMustBeAnAddress(string upstream, bool ok)
    {
        var errors = RouteErrors(new ProxyRoute { Host = "app.example.com", Upstream = upstream });
        Assert.Equal(ok, errors.Count == 0);
    }

    /// <summary>
    /// A certificate from files needs its files, and an HTTP challenge needs a name the internet reaches.
    /// True: the route can get the certificate it asks for.
    /// False: the route would never serve HTTPS.
    /// </summary>
    [Fact]
    public void ValidateRoute_CertificateNeedsWhatItUses()
    {
        var pfx = new ProxyRoute { Host = "media.example.com" };
        pfx.Certificate.Mode = CertificateMode.Pkcs12File;
        Assert.NotEmpty(RouteErrors(pfx));
        var pem = new ProxyRoute { Host = "media.example.com" };
        pem.Certificate.Mode = CertificateMode.PemFiles;
        pem.Certificate.Path = "/etc/cert.pem";
        Assert.NotEmpty(RouteErrors(pem));
        var wildcard = new ProxyRoute { Host = "*.example.com" };
        wildcard.Certificate.Challenge = CertificateChallenge.Http;
        Assert.NotEmpty(RouteErrors(wildcard));
        var lan = new ProxyRoute { Host = "home.example.com", LocalOnly = true };
        lan.Certificate.Challenge = CertificateChallenge.Dns;
        Assert.Empty(RouteErrors(lan));
    }

    /// <summary>
    /// Routes survive a round trip through the settings file.
    /// True: a restart keeps every route.
    /// False: routes would be lost when Jellyfin restarts.
    /// </summary>
    [Fact]
    public void SavedSettings_KeepTheirRoutes()
    {
        var config = new Jellyfin.Plugin.Necto.Configuration.PluginConfiguration();
        config.Proxy.Routes.Add(new ProxyRoute { Id = "a", Host = "media.example.com", LocalOnly = true, Upstream = "http://192.168.1.20:8096" });
        config.Proxy.Routes[0].Certificate.Challenge = CertificateChallenge.Dns;
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Jellyfin.Plugin.Necto.Configuration.PluginConfiguration));
        using var writer = new System.IO.StringWriter();
        serializer.Serialize(writer, config);
        using var reader = new System.IO.StringReader(writer.ToString());
        var back = (Jellyfin.Plugin.Necto.Configuration.PluginConfiguration)serializer.Deserialize(reader)!;

        var route = Assert.Single(back.Proxy.Routes);
        Assert.Equal("media.example.com", route.Host);
        Assert.True(route.LocalOnly);
        Assert.False(route.IsJellyfin);
        Assert.Equal(CertificateChallenge.Dns, route.Certificate.Challenge);
    }
}
