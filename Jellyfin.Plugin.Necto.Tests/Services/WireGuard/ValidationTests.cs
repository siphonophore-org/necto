using System;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

public class ValidationTests
{
    private static readonly string KeyA = TunnelKeys.NewPrivateKey();
    private static readonly string KeyB = TunnelKeys.NewPrivateKey();

    private static WireGuardSettings Host() => new() { Enabled = true, HostEnabled = true, TunnelSubnet = "10.66.0.0/24", HostPublicKey = KeyA };

    [Fact]
    public void RefusesTwoPeersOnOneKey()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2" });
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.3" });

        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));
    }

    [Fact]
    public void RefusesAPeerOnTheHostKey()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyA, Address = "10.66.0.2" });

        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));
    }

    [Theory]
    [InlineData("10.66.0.0/24")]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.66.0.128/25")]
    public void RefusesAChildNetworkOverTheTunnel(string child)
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2", RoutedSubnets = { child } });

        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));
    }

    [Fact]
    public void AcceptsAChildNetworkBesideTheTunnel()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2", RoutedSubnets = { "192.168.1.0/24" } });

        WireGuardValidator.ValidateAddresses(config);
    }

    [Fact]
    public void RefusesTwoPeersOnOneIdentifier()
    {
        var config = Host();
        var id = Guid.NewGuid();
        config.Peers.Add(new TunnelPeer { Id = id, PublicKey = KeyB, Address = "10.66.0.2" });
        config.Peers.Add(new TunnelPeer { Id = id, PublicKey = TunnelKeys.NewPrivateKey(), Address = "10.66.0.3" });

        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));
    }

    [Fact]
    public void ComparesAddressesAsAddresses()
    {
        var config = Host();
        config.Peers.Add(new TunnelPeer { PublicKey = KeyB, Address = "10.66.0.2" });
        config.Peers.Add(new TunnelPeer { PublicKey = TunnelKeys.NewPrivateKey(), Address = " 10.66.0.2 " });

        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));
    }

    [Theory]
    [InlineData("0.0.0.0/0", false)]
    [InlineData("10.0.0.0/7", false)]
    [InlineData("10.0.0.0/8", true)]
    [InlineData("10.66.0.0/30", true)]
    [InlineData(" 10.66.0.0/24 ", true)]
    public void TunnelSubnetRunsFromSlash8ToSlash30(string subnet, bool expected)
        => Assert.Equal(expected, SidecarConfigBuilder.TryHostAddress(subnet, out _));

    [Fact]
    public void UnownedPeersAreNotOneUser()
    {
        var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        using var usage = new UsageTracker(null, () => now);
        var config = new WireGuardSettings();
        var a = new TunnelPeer { PublicKey = "a" };
        var b = new TunnelPeer { PublicKey = "b" };
        config.Peers.Add(a);
        config.Peers.Add(b);
        usage.Record("host", new[] { new PeerStatus { PublicKey = "a", RxBytes = 6_000_000_000 }, new PeerStatus { PublicKey = "b", RxBytes = 6_000_000_000 } }, k => k == "a" ? a.Id : b.Id);

        Assert.Empty(usage.OverLimit(config, _ => (10_000_000_000, LimitPeriod.Month)));
    }
}
