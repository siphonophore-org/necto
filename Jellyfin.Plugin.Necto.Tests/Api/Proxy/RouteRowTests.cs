using System;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Proxy;

/// <summary>
/// A route's row state reads Off while the route is off, else its certificate's state, as its badge shows it.
/// </summary>
public class RouteRowTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private static ProxyRoute Route(CertificateMode mode = CertificateMode.Automatic, bool enabled = true)
        => new() { Id = "r", Host = "media.example.com", Enabled = enabled, Certificate = new ProxyCertificateSettings { Mode = mode } };

    /// <summary>
    /// Each certificate state gives its own row state, and Off comes first.
    /// True: the table's filter finds a route by how its certificate stands.
    /// False: a failed certificate could hide under Ready, or a route that is off read as Ready.
    /// </summary>
    [Fact]
    public void StateOf_FollowsTheCertificate()
    {
        var loaded = new RouteStatus { Certificate = new CertificateStatus { Loaded = true, NotAfterUtc = Now.AddDays(60) } };
        Assert.Equal(RouteRow.Off, RouteRow.StateOf(Route(enabled: false), loaded, Now));
        Assert.Equal(RouteRow.NoCertificate, RouteRow.StateOf(Route(CertificateMode.None), loaded, Now));
        Assert.Equal(RouteRow.Ready, RouteRow.StateOf(Route(), loaded, Now));
        Assert.Equal(RouteRow.ExpiresSoon, RouteRow.StateOf(Route(), new RouteStatus { Certificate = new CertificateStatus { Loaded = true, NotAfterUtc = Now.AddDays(3) } }, Now));
        Assert.Equal(RouteRow.Requesting, RouteRow.StateOf(Route(), new RouteStatus { Acme = new() { Issuing = true } }, Now));
        Assert.Equal(RouteRow.Failed, RouteRow.StateOf(Route(), new RouteStatus { Acme = new() { LastError = "rate limited" } }, Now));
        Assert.Equal(RouteRow.Failed, RouteRow.StateOf(Route(), new RouteStatus { Certificate = new CertificateStatus { Error = "bad key" } }, Now));
        Assert.Equal(RouteRow.Waiting, RouteRow.StateOf(Route(), null, Now));
    }
}
