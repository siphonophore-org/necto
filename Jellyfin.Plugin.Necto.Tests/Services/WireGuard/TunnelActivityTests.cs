using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// Each tunnel event is written to the activity log and sent to Notifications under its WireGuard key.
/// </summary>
public class TunnelActivityTests
{
    private static (TunnelActivity Activity, FakeActivity Log, FakeNotifier Notifier, TunnelPeer Peer, TunnelConnection Connection) Build()
    {
        var users = new FakeUsers();
        var viewer = users.Add("viewer");
        var peer = new TunnelPeer { Name = "Phone", UserId = viewer.Id, PublicKey = "key-of-the-phone" };
        var connection = new TunnelConnection { Name = "Friend" };
        var store = new FakeConfigurationStore(new PluginConfiguration { WireGuard = new WireGuardSettings { Peers = { peer }, Connections = { connection } } });
        var log = new FakeActivity();
        var notifier = new FakeNotifier();
        return (new TunnelActivity(log.Logger, notifier, store, users.Manager, FakeServerConfig.Create()), log, notifier, peer, connection);
    }

    /// <summary>
    /// A peer coming up and going down sends wireguard.connected and wireguard.disconnected about its owner.
    /// True: an owner's groups decide whether they hear about their own peers.
    /// False: connections never reach Notifications, or reach the wrong person.
    /// </summary>
    [Fact]
    public void PeerUpAndDown_SendTheirEventsAboutTheOwner()
    {
        var (activity, log, notifier, peer, _) = Build();

        activity.PeerUp("host", peer.PublicKey, "198.51.100.7:40000");
        activity.PeerDown("host", peer.PublicKey);

        Assert.Equal(new[] { WireGuardEvents.Connected, WireGuardEvents.Disconnected }, notifier.Events.Select(e => e.Key));
        Assert.All(notifier.Events, e => Assert.Equal(peer.UserId, e.UserId));
        Assert.Equal("viewer connected on Phone from 198.51.100.7", notifier.Events[0].Body);
        Assert.Equal(new[] { "Peer connected", "Peer disconnected" }, log.Names);
    }

    /// <summary>
    /// A connection, a data limit, and a failing tunnel each send their own key, with the failure as an error.
    /// True: every WireGuard alert goes through Notifications.
    /// False: an alert is only in the activity log.
    /// </summary>
    [Fact]
    public void ConnectionLimitAndFailure_SendTheirEvents()
    {
        var (activity, _, notifier, peer, connection) = Build();
        var key = connection.Id.ToString("N");

        activity.PeerUp(key, "remote", "203.0.113.5:51820");
        activity.PeerDown(key, "remote");
        activity.DataLimitReached(peer.Id);
        activity.TunnelFailed("host", "address already in use");

        Assert.Equal(
            new[] { WireGuardEvents.ConnectionEstablished, WireGuardEvents.ConnectionLost, WireGuardEvents.DataLimitReached, WireGuardEvents.TunnelFailed },
            notifier.Events.Select(e => e.Key));
        Assert.Equal(NectoEventLevel.Error, notifier.Events[3].Level);
        Assert.Equal(NectoEventLevel.Warning, notifier.Events[1].Level);
        Assert.All(WireGuardEvents.All.Keys, k => Assert.StartsWith("wireguard.", k, StringComparison.Ordinal));
    }

    /// <summary>
    /// The address of an endpoint drops its port, for IPv4, bracketed IPv6, and a bare IPv6 address.
    /// True: the log and the map show only the address.
    /// False: an IPv6 address is cut at its first colon.
    /// </summary>
    [Theory]
    [InlineData("198.51.100.7:40000", "198.51.100.7")]
    [InlineData("[2001:db8::1]:51820", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("vpn.example.com", "vpn.example.com")]
    [InlineData(null, "?")]
    public void HostOf_DropsThePort(string? endpoint, string expected)
        => Assert.Equal(expected, TunnelActivity.HostOf(endpoint));
}
