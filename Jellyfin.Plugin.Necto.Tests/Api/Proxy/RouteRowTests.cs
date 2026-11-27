using System;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Proxy;

/// <summary>
/// A route's row reads Off, Untrusted, HTTP, Incomplete, or HTTPS, and its detail says what is not finished or soon due.
/// </summary>
public class RouteRowTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    private static ProxyRoute Route(CertificateMode mode = CertificateMode.Automatic, bool enabled = true, string upstream = "http://127.0.0.1:2342")
        => new() { Id = "r", Host = "media.example.com", Enabled = enabled, Upstream = upstream, Certificate = new ProxyCertificateSettings { Mode = mode } };

    private static RouteStatus Loaded(int days) => new() { Certificate = new CertificateStatus { Loaded = true, NotAfterUtc = Now.AddDays(days) } };

    /// <summary>
    /// Each certificate state maps onto the four states, with Off first.
    /// True: the table's filter finds a route by whether it serves HTTPS.
    /// False: a failed certificate could read as HTTPS, or a route that is off as anything else.
    /// </summary>
    [Fact]
    public void StateOf_MapsTheCertificate()
    {
        var trusted = TakeoverPhase.Managed;
        Assert.Equal(RouteRow.Off, RouteRow.StateOf(Route(enabled: false), Loaded(60), trusted));
        Assert.Equal(RouteRow.Http, RouteRow.StateOf(Route(CertificateMode.None), null, trusted));
        Assert.Equal(RouteRow.Https, RouteRow.StateOf(Route(), Loaded(60), trusted));
        Assert.Equal(RouteRow.Https, RouteRow.StateOf(Route(), Loaded(3), trusted));
        Assert.Equal(RouteRow.Incomplete, RouteRow.StateOf(Route(), new RouteStatus { Acme = new() { Issuing = true } }, trusted));
        Assert.Equal(RouteRow.Incomplete, RouteRow.StateOf(Route(), new RouteStatus { Acme = new() { LastError = "rate limited" } }, trusted));
        Assert.Equal(RouteRow.Incomplete, RouteRow.StateOf(Route(), null, trusted));

        Assert.Equal(RouteRow.ExpiresSoon, RouteRow.DetailOf(Route(), Loaded(3), trusted, Now));
        Assert.Equal(string.Empty, RouteRow.DetailOf(Route(), Loaded(60), trusted, Now));
        Assert.Equal(RouteRow.Requesting, RouteRow.DetailOf(Route(), new RouteStatus { Acme = new() { Issuing = true } }, trusted, Now));
        Assert.Equal(RouteRow.Failed, RouteRow.DetailOf(Route(), new RouteStatus { Certificate = new CertificateStatus { Error = "bad key" } }, trusted, Now));
        Assert.Equal(RouteRow.Waiting, RouteRow.DetailOf(Route(), null, trusted, Now));
    }

    /// <summary>
    /// A route to Jellyfin reads Untrusted until Jellyfin trusts the proxy, and Incomplete while Jellyfin restarts to.
    /// True: the routes table shows a Jellyfin route visitors cannot use yet.
    /// False: a Jellyfin route would read HTTPS while Jellyfin turns its visitors away.
    /// </summary>
    [Fact]
    public void StateOf_RouteToJellyfinWaitsForTrust()
    {
        var jellyfin = Route(upstream: string.Empty);
        Assert.Equal(RouteRow.Untrusted, RouteRow.StateOf(jellyfin, Loaded(60), TakeoverPhase.Unmanaged));
        Assert.Equal(RouteRow.Untrusted, RouteRow.StateOf(jellyfin, Loaded(60), TakeoverPhase.Degraded));
        Assert.Equal(RouteRow.Incomplete, RouteRow.StateOf(jellyfin, Loaded(60), TakeoverPhase.PendingRestart));
        Assert.Equal(RouteRow.Restarting, RouteRow.DetailOf(jellyfin, Loaded(60), TakeoverPhase.PendingRestart, Now));
        Assert.Equal(RouteRow.Https, RouteRow.StateOf(jellyfin, Loaded(60), TakeoverPhase.Managed));
        Assert.Equal(RouteRow.Http, RouteRow.StateOf(Route(CertificateMode.None), null, TakeoverPhase.Unmanaged));
    }
}
