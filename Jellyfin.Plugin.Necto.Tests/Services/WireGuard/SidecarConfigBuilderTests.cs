using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

public class SidecarConfigBuilderTests
{
    private static readonly string KeyA = TunnelKeys.NewPrivateKey();
    private static readonly string KeyB = TunnelKeys.NewPrivateKey();

    private static string Same(string value) => value;

    private static WireGuardSettings Host() => new()
    {
        Enabled = true,
        HostEnabled = true,
        ListenPort = 51820,
        TunnelSubnet = "10.66.0.0/24",
        HostPrivateKey = KeyA,
        ForwardClientAddress = true
    };

    [Theory]
    [InlineData("10.66.0.0/24", "10.66.0.1")]
    [InlineData("10.66.0.77/24", "10.66.0.1")]
    [InlineData("172.20.8.0/22", "172.20.8.1")]
    [InlineData("192.168.200.64/30", "192.168.200.65")]
    public void TryHostAddress_TakesTheFirstHost(string subnet, string expected)
    {
        Assert.True(SidecarConfigBuilder.TryHostAddress(subnet, out var address));
        Assert.Equal(expected, address.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("10.66.0.1")]
    [InlineData("10.66.0.0/31")]
    [InlineData("fd00::/64")]
    [InlineData("not a subnet")]
    public void TryHostAddress_RefusesWhatCannotHoldPeers(string subnet)
        => Assert.False(SidecarConfigBuilder.TryHostAddress(subnet, out _));

    [Fact]
    public void Host_ForwardsJellyfinAndRoutesEachPeer()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2", RoutedSubnets = { "192.168.50.0/24", "junk" } });

        var server = SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey];

        Assert.Equal(new[] { "10.66.0.1" }, server.Addresses);
        Assert.Equal(51820, server.ListenPort);
        var peer = Assert.Single(server.Peers);
        Assert.Equal(new[] { "10.66.0.2/32", "192.168.50.0/24" }, peer.AllowedIPs);
        var inbound = Assert.Single(server.Inbound);
        Assert.Equal(8096, inbound.Port);
        Assert.Equal("127.0.0.1:8096", inbound.Target);
        Assert.True(inbound.Http);
        Assert.True(inbound.ForwardFor);
    }

    [Fact]
    public void Host_LeavesOutDisabledAndBrokenPeers()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2", Enabled = false });
        config.Peers.Add(new TunnelPeer { PublicKey = "short", Address = "10.66.0.3" });
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "nowhere" });

        Assert.Empty(SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey].Peers);
    }

    [Fact]
    public void Host_DoesNotRunWhenOffOrWithoutAKey()
    {
        var off = Host();
        off.HostEnabled = false;
        var keyless = Host();
        keyless.HostPrivateKey = string.Empty;

        Assert.Empty(SidecarConfigBuilder.Build(off, Same, 8096));
        Assert.Empty(SidecarConfigBuilder.Build(keyless, Same, 8096));
    }

    [Fact]
    public void Host_DecryptsTheStoredKey()
    {
        var config = Host();
        config.HostPrivateKey = "enc:" + KeyA;

        var server = SidecarConfigBuilder.Build(config, v => v.Replace("enc:", string.Empty, StringComparison.Ordinal), 8096)[SidecarConfigBuilder.HostKey];

        Assert.Equal(KeyA, server.PrivateKey);
    }

    [Fact]
    public void Connection_OpensItsForwardsAndKeepsAlive()
    {
        var config = new WireGuardSettings { Enabled = true, ConnectEnabled = true };
        var connection = new TunnelConnection
        {
            Name = "Friend",
            Endpoint = "vpn.example.com:51820",
            PeerPublicKey = KeyB,
            PrivateKey = KeyA,
            Address = "10.0.0.5",
            Forwards =
            {
                new PortForward { LocalPort = 18096, RemoteHost = "10.0.0.1", RemotePort = 8096 },
                new PortForward { LocalPort = 18097, RemoteHost = "jellyfin.example.com", RemotePort = 8096 }
            }
        };
        config.Connections.Add(connection);

        var tunnel = SidecarConfigBuilder.Build(config, Same, 8096)[connection.Id.ToString("N")];

        var peer = Assert.Single(tunnel.Peers);
        Assert.Equal("vpn.example.com:51820", peer.Endpoint);
        Assert.Equal(25, peer.Keepalive);
        Assert.Null(peer.PresharedKey);
        var forward = Assert.Single(tunnel.Outbound);
        Assert.Equal("127.0.0.1:18096", forward.Listen);
        Assert.Equal("10.0.0.1:8096", forward.Target);
        Assert.Empty(tunnel.Inbound);
    }

    [Fact]
    public void Connection_IsLeftOutWhenDisabledOrIncomplete()
    {
        var config = new WireGuardSettings { Enabled = true, ConnectEnabled = true };
        config.Connections.Add(new TunnelConnection { Enabled = false, Endpoint = "vpn.example.com:51820", PeerPublicKey = KeyB, PrivateKey = KeyA, Address = "10.0.0.5" });
        config.Connections.Add(new TunnelConnection { Endpoint = string.Empty, PeerPublicKey = KeyB, PrivateKey = KeyA, Address = "10.0.0.5" });
        config.Connections.Add(new TunnelConnection { Endpoint = "vpn.example.com:51820", PeerPublicKey = KeyB, PrivateKey = KeyA, Address = string.Empty });

        Assert.Empty(SidecarConfigBuilder.Build(config, Same, 8096));
    }

    [Fact]
    public void NewPrivateKey_IsClampedAndUnique()
    {
        var keys = Enumerable.Range(0, 20).Select(_ => TunnelKeys.NewPrivateKey()).ToList();

        Assert.Equal(20, keys.Distinct().Count());
        foreach (var key in keys)
        {
            var raw = Convert.FromBase64String(key);
            Assert.Equal(32, raw.Length);
            Assert.Equal(0, raw[0] & 7);
            Assert.Equal(64, raw[31] & 192);
        }
    }
}
