using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Api.WireGuard;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.WireGuard;

/// <summary>
/// The map list joins every stored peer and connection with what the tunnels report, with where each connects from.
/// </summary>
public class PeerMapTests
{
    /// <summary>
    /// The host leads, each peer carries its owner, state, and address without the port, and each connection its remote host.
    /// True: the Dashboard map places every tunnel end and names it.
    /// False: a peer is missing from the map, or placed by an address with a port.
    /// </summary>
    [Fact]
    public void Build_JoinsPeersAndConnectionsWithTheirState()
    {
        var owner = Guid.NewGuid();
        var online = new TunnelPeer { Name = "Phone", UserId = owner, PublicKey = "k1" };
        var over = new TunnelPeer { Name = "TV", UserId = owner, PublicKey = "k2" };
        var refused = new TunnelPeer { Name = "Laptop", UserId = owner, PublicKey = "k3" };
        var connection = new TunnelConnection { Name = "Friend", Endpoint = "vpn.example.com:51820" };
        var settings = new WireGuardSettings
        {
            Enabled = true,
            HostEnabled = true,
            ConnectEnabled = true,
            PublicEndpoint = "203.0.113.9",
            Peers = { online, over, refused },
            Connections = { connection }
        };
        var tunnels = new List<TunnelStatus>
        {
            new() { Key = "host", State = "Running", Peers = { new PeerStatus { PublicKey = "k1", Endpoint = "198.51.100.7:40000", Connected = true, RxBytes = 5, TxBytes = 6 } } },
            new() { Key = connection.Id.ToString("N"), State = "Failed" }
        };

        var map = PeerMap.Build(settings, tunnels, new[] { over.Id }, new Dictionary<Guid, PeerRefusal> { [refused.Id] = PeerRefusal.NotAllowed }, id => id == owner ? "viewer" : null);

        Assert.Equal(new[] { "Host", "Peer", "Peer", "Peer", "Connection" }, map.Select(m => m.Kind));
        Assert.Equal("203.0.113.9", map[0].RemoteAddress);
        Assert.True(map[0].Connected);
        Assert.Equal(("viewer", "198.51.100.7", "Connected", 11L), (map[1].UserName, map[1].RemoteAddress, map[1].State, map[1].Bytes));
        Assert.Equal("OverLimit", map[2].State);
        Assert.Equal("NotAllowed", map[3].State);
        Assert.Equal(("vpn.example.com", "Failed"), (map[4].RemoteAddress, map[4].State));
    }

    /// <summary>
    /// With the host off, no host entry is listed, and a peer of a host that is off reads as disabled.
    /// True: the map shows only what runs.
    /// False: the map shows a host that does not run.
    /// </summary>
    [Fact]
    public void Build_HostOff_ListsNoHost()
    {
        var settings = new WireGuardSettings { Enabled = true, Peers = { new TunnelPeer { Name = "Phone", PublicKey = "k" } } };

        var map = PeerMap.Build(settings, new List<TunnelStatus>(), Array.Empty<Guid>(), new Dictionary<Guid, PeerRefusal>(), _ => null);

        Assert.Equal("Disabled", Assert.Single(map).State);
    }
}
