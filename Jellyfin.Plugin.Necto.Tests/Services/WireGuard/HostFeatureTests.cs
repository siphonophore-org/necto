using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

public class HostFeatureTests
{
    private static readonly string HostKey = TunnelKeys.NewPrivateKey();
    private static readonly string PeerKey = TunnelKeys.NewPrivateKey();
    private static readonly string HostPublic = TunnelKeys.NewPrivateKey();
    private static readonly string PeerPublic = TunnelKeys.NewPrivateKey();
    private static readonly string Psk = TunnelKeys.NewPresharedKey();

    private static string Same(string value) => value;

    private static WireGuardSettings Host() => new()
    {
        Enabled = true,
        HostEnabled = true,
        TunnelSubnet = "10.66.0.0/24",
        PublicEndpoint = "vpn.example.com",
        HostPrivateKey = HostKey,
        HostPublicKey = HostPublic
    };

    private static TunnelPeer Peer() => new()
    {
        Name = "Phone",
        PublicKey = PeerPublic,
        PrivateKey = PeerKey,
        PresharedKey = Psk,
        Address = "10.66.0.2"
    };

    [Fact]
    public void PeerConfig_RoutesOnlyTheHost()
    {
        var text = PeerConfigWriter.Write(Host(), Peer(), Same)!;

        Assert.Contains("PrivateKey = " + PeerKey + "\n", text, StringComparison.Ordinal);
        Assert.Contains("Address = 10.66.0.2/32\n", text, StringComparison.Ordinal);
        Assert.Contains("PublicKey = " + HostPublic + "\n", text, StringComparison.Ordinal);
        Assert.Contains("PresharedKey = " + Psk + "\n", text, StringComparison.Ordinal);
        Assert.Contains("Endpoint = vpn.example.com:51820\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.66.0.1/32\n", text, StringComparison.Ordinal);
        Assert.Contains("PersistentKeepalive = 25\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DNS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MTU", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PeerConfig_PointsDnsAtTheHostWithoutWideningTheRoutes()
    {
        var config = Host();
        config.DnsEnabled = true;

        var text = PeerConfigWriter.Write(config, Peer(), Same)!;

        Assert.Contains("DNS = 10.66.0.1\n", text, StringComparison.Ordinal);
        Assert.Contains("AllowedIPs = 10.66.0.1/32\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PeerConfig_LeavesOutThePresharedKeyWhenOff()
    {
        var config = Host();
        config.UsePresharedKeys = false;

        Assert.DoesNotContain("PresharedKey", PeerConfigWriter.Write(config, Peer(), Same), StringComparison.Ordinal);
    }

    [Fact]
    public void PeerConfig_MarksAPeerThatBroughtItsOwnKey()
    {
        var peer = Peer();
        peer.PrivateKey = string.Empty;

        var text = PeerConfigWriter.Write(Host(), peer, Same)!;

        Assert.DoesNotContain("PrivateKey = ", text, StringComparison.Ordinal);
        Assert.Contains("# PrivateKey", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PeerConfig_IsNullWithoutAHostKey()
    {
        var config = Host();
        config.HostPublicKey = string.Empty;

        Assert.Null(PeerConfigWriter.Write(config, Peer(), Same));
    }

    [Theory]
    [InlineData("vpn.example.com", 51820, "vpn.example.com:51820")]
    [InlineData("vpn.example.com:443", 51820, "vpn.example.com:443")]
    [InlineData("203.0.113.10", 51000, "203.0.113.10:51000")]
    [InlineData("2001:db8::1", 51820, "[2001:db8::1]:51820")]
    [InlineData("[2001:db8::1]:443", 51820, "[2001:db8::1]:443")]
    public void Endpoint_AddsTheListenPortWhenMissing(string endpoint, int port, string expected)
        => Assert.Equal(expected, PeerConfigWriter.Endpoint(new WireGuardSettings { PublicEndpoint = endpoint, ListenPort = port }));

    [Fact]
    public void NextFreeAddress_SkipsTheHostAndTakenAddresses()
    {
        Assert.Equal("10.66.0.2", SidecarConfigBuilder.NextFreeAddress("10.66.0.0/24", Array.Empty<string>()));
        Assert.Equal("10.66.0.4", SidecarConfigBuilder.NextFreeAddress("10.66.0.0/24", new[] { "10.66.0.2", "10.66.0.3" }));
        Assert.Null(SidecarConfigBuilder.NextFreeAddress("10.66.0.0/30", new[] { "10.66.0.2" }));
    }

    [Theory]
    [InlineData("10.66.0.2", true)]
    [InlineData("10.66.0.254", true)]
    [InlineData("10.66.0.1", false)]
    [InlineData("10.66.0.0", false)]
    [InlineData("10.66.0.255", false)]
    [InlineData("10.67.0.2", false)]
    public void IsPeerAddress_RefusesTheHostNetworkAndBroadcast(string address, bool expected)
        => Assert.Equal(expected, SidecarConfigBuilder.IsPeerAddress("10.66.0.0/24", address));

    [Fact]
    public void Host_LimitsAPeerByItsOwnLimitOrTheDefault()
    {
        var config = Host();
        config.PeerDownloadMbps = 20;
        config.PeerUploadMbps = 5;
        var own = Peer();
        own.DownloadMbps = 100;
        own.UploadMbps = 0;
        var fallback = Peer();
        fallback.PublicKey = TunnelKeys.NewPrivateKey();
        fallback.Address = "10.66.0.3";
        config.Peers.Add(own);
        config.Peers.Add(fallback);

        var peers = SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey].Peers;

        Assert.Equal(100, peers[0].SendMbps);
        Assert.Equal(0, peers[0].RecvMbps);
        Assert.Equal(20, peers[1].SendMbps);
        Assert.Equal(5, peers[1].RecvMbps);
    }

    [Fact]
    public void Host_AnswersDnsOnlyWhenOn()
    {
        var config = Host();
        Assert.Null(SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey].Dns);

        config.DnsEnabled = true;
        config.DnsName = "Jellyfin.Internal.";
        config.DnsUpstreams = "1.1.1.1, 9.9.9.9:5353, junk";
        var dns = SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey].Dns!;

        Assert.Equal(new[] { "jellyfin.internal" }, dns.Names);
        Assert.Equal(new[] { "1.1.1.1:53", "9.9.9.9:5353" }, dns.Upstreams);
    }

    [Fact]
    public void Host_LeavesOutBlockedPeers()
    {
        var config = Host();
        var peer = Peer();
        config.Peers.Add(peer);

        var peers = SidecarConfigBuilder.Build(config, Same, 8096, new HashSet<Guid> { peer.Id })[SidecarConfigBuilder.HostKey].Peers;

        Assert.Empty(peers);
    }

    [Fact]
    public void WithoutPeers_TellsAPeerChangeFromARestart()
    {
        var config = Host();
        var before = SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey];
        config.Peers.Add(Peer());
        var after = SidecarConfigBuilder.Build(config, Same, 8096)[SidecarConfigBuilder.HostKey];

        Assert.Equal(Json(SidecarConfigBuilder.WithoutPeers(before)), Json(SidecarConfigBuilder.WithoutPeers(after)));
        Assert.NotEqual(Json(before), Json(after));
    }

    private static string Json(SidecarConfig config) => System.Text.Json.JsonSerializer.Serialize(config, SidecarProcess.ConfigJson);

    [Fact]
    public void Usage_CountsGrowthAndSidecarRestarts()
    {
        var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        using var usage = new UsageTracker(null, () => now);
        var id = Guid.NewGuid();
        Guid? IdOf(string key) => key == "k" ? id : null;

        usage.Record("host", new[] { new PeerStatus { PublicKey = "k", RxBytes = 100, TxBytes = 50 } }, IdOf);
        usage.Record("host", new[] { new PeerStatus { PublicKey = "k", RxBytes = 300, TxBytes = 50 } }, IdOf);
        usage.Record("host", new[] { new PeerStatus { PublicKey = "k", RxBytes = 10, TxBytes = 0 } }, IdOf);

        foreach (var period in Enum.GetValues<LimitPeriod>())
        {
            Assert.Equal(360, usage.BytesOf(id, period));
        }
    }

    [Theory]
    [InlineData(LimitPeriod.Hour, "2026-11-30T23", "2026-12-01T00")]
    [InlineData(LimitPeriod.Day, "2026-11-30", "2026-12-01")]
    [InlineData(LimitPeriod.Week, "2026-W49", "2026-W49")]
    [InlineData(LimitPeriod.Month, "2026-11", "2026-12")]
    [InlineData(LimitPeriod.Year, "2026", "2026")]
    public void WindowOf_NamesEachWindowInUtc(LimitPeriod period, string before, string after)
    {
        // Monday 30 November 2026, 23:59, then a minute later in December.
        Assert.Equal(before, UsageTracker.WindowOf(period, new DateTime(2026, 11, 30, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(after, UsageTracker.WindowOf(period, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Usage_EachPeriodStartsOverWhenItsWindowEnds()
    {
        var now = new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc);
        using var usage = new UsageTracker(null, () => now);
        var id = Guid.NewGuid();
        usage.Record("host", new[] { new PeerStatus { PublicKey = "k", RxBytes = 500 } }, _ => id);

        now = new DateTime(2026, 12, 1, 0, 0, 1, DateTimeKind.Utc);

        Assert.Equal(0, usage.BytesOf(id, LimitPeriod.Hour));
        Assert.Equal(0, usage.BytesOf(id, LimitPeriod.Day));
        Assert.Equal(0, usage.BytesOf(id, LimitPeriod.Month));
        Assert.Equal(500, usage.BytesOf(id, LimitPeriod.Week));
        Assert.Equal(500, usage.BytesOf(id, LimitPeriod.Year));
    }

    [Fact]
    public void Usage_FindsPeersOverTheirOwnTheDefaultOrTheirGroupsLimit()
    {
        var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        using var usage = new UsageTracker(null, () => now);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var config = Host();
        config.PeerDataLimit = 10_000_000_000;
        config.PeerDataPeriod = LimitPeriod.Month;
        var own = new TunnelPeer { UserId = alice, PublicKey = "a1", DataLimit = 1_000_000_000, DataPeriod = LimitPeriod.Hour };
        var underDefault = new TunnelPeer { UserId = alice, PublicKey = "a2" };
        var bob1 = new TunnelPeer { UserId = bob, PublicKey = "b1" };
        var bob2 = new TunnelPeer { UserId = bob, PublicKey = "b2" };
        config.Peers.AddRange(new[] { own, underDefault, bob1, bob2 });
        var byKey = config.Peers.ToDictionary(p => p.PublicKey, p => p.Id);

        usage.Record(
            "host",
            new[]
            {
                new PeerStatus { PublicKey = "a1", RxBytes = 2_000_000_000 },
                new PeerStatus { PublicKey = "a2", RxBytes = 9_000_000_000 },
                new PeerStatus { PublicKey = "b1", RxBytes = 8_000_000_000 },
                new PeerStatus { PublicKey = "b2", RxBytes = 8_000_000_000 }
            },
            key => byKey[key]);

        // a1 is over its own 1 GB an hour. a2 is under the 10 GB a month default, and alice is under
        // 15 GB a week together. Bob's peers are each under 10 GB, but together over 15 GB, so both are cut off.
        Assert.Equal(new[] { own.Id, bob1.Id, bob2.Id }.OrderBy(x => x), usage.OverLimit(config, _ => (15_000_000_000, LimitPeriod.Week)).OrderBy(x => x));

        // An hour later a1's hourly window has ended, so it is back.
        now = now.AddHours(1);
        Assert.Equal(new[] { bob1.Id, bob2.Id }.OrderBy(x => x), usage.OverLimit(config, _ => (15_000_000_000, LimitPeriod.Week)).OrderBy(x => x));
    }

    [Fact]
    public void Usage_ConnectionUsesItsOwnLimitOrTheDefault()
    {
        var now = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        using var usage = new UsageTracker(null, () => now);
        var config = new WireGuardSettings { ConnectionDataLimit = 5_000_000_000, ConnectionDataPeriod = LimitPeriod.Day };
        var byDefault = new TunnelConnection();
        var unlimited = new TunnelConnection { DataLimit = 0 };
        config.Connections.AddRange(new[] { byDefault, unlimited });

        usage.Record(byDefault.Id.ToString("N"), new[] { new PeerStatus { PublicKey = "p", RxBytes = 6_000_000_000 } }, _ => byDefault.Id);
        usage.Record(unlimited.Id.ToString("N"), new[] { new PeerStatus { PublicKey = "p", RxBytes = 6_000_000_000 } }, _ => unlimited.Id);

        Assert.Equal(new[] { byDefault.Id }, usage.OverLimit(config));
    }

    [Fact]
    public void Validator_RefusesDuplicateAddressesAndBadDns()
    {
        var config = Host();
        config.Peers.Add(Peer());
        config.Peers.Add(Peer());
        Assert.Throws<ArgumentException>(() => WireGuardValidator.ValidateAddresses(config));

        var dns = Host();
        dns.DnsEnabled = true;
        dns.DnsName = "not a name";
        Assert.Throws<ArgumentException>(() => WireGuardValidator.Validate(dns));

        var upstream = Host();
        upstream.DnsEnabled = true;
        upstream.DnsUpstreams = "resolver.example.com";
        Assert.Throws<ArgumentException>(() => WireGuardValidator.Validate(upstream));
    }

    [Fact]
    public void Validator_AcceptsTheDefaults()
    {
        WireGuardValidator.Validate(new WireGuardSettings());
        WireGuardValidator.Validate(Host());
        WireGuardValidator.ValidateAddresses(Host());
    }
}
