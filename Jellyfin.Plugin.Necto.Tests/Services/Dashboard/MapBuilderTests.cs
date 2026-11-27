using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Api.WireGuard;
using Jellyfin.Plugin.Necto.Services.Dashboard;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Dashboard;

/// <summary>
/// The Dashboard map counts the proxy's visitors only while the proxy is on and the tunnel ends only while WireGuard
/// is on, places each tunnel end by its public address, and marks this server's country.
/// </summary>
public class MapBuilderTests
{
    private static readonly GeoLocation Us = new("US", "United States", string.Empty);
    private static readonly GeoLocation De = new("DE", "Germany", string.Empty);

    private static List<CountryTotal> Totals(params (string Code, string Name, int Count)[] totals)
        => totals.Select(t => new CountryTotal { CountryCode = t.Code, Country = t.Name, Count = t.Count }).ToList();

    private static GeoLocation Locate(string? address) => address switch
    {
        "198.51.100.7" => De,
        "203.0.113.9" => Us,
        _ => GeoLocation.Unknown
    };

    private static List<PeerConnection> Ends() => new()
    {
        new() { Kind = "Host", Name = "Host", RemoteAddress = "vpn.example.com", State = "Running", Connected = true },
        new() { Kind = "Peer", Name = "Phone", RemoteAddress = "198.51.100.7", State = "Connected", Connected = true },
        new() { Kind = "Peer", Name = "Laptop", RemoteAddress = null, State = "Offline" },
        new() { Kind = "Connection", Name = "Office", RemoteAddress = "203.0.113.9", State = "Connected", Connected = true }
    };

    /// <summary>
    /// With both tools on, every layer counts, the host marks the server, and the busiest country leads.
    /// True: the map shows visitors and tunnels together, each kind in its own layer.
    /// False: the host would count as a peer, or a layer would be lost.
    /// </summary>
    [Fact]
    public void Build_BothOn_CountsEveryLayer()
    {
        var map = MapBuilder.Build(true, true, Us, Totals(("US", "United States", 4)), Totals(("DE", "Germany", 1)), Totals(("CN", "China", 9)), Ends(), Locate);

        Assert.True(map.Proxy);
        Assert.True(map.WireGuard);
        Assert.Equal(new[] { "CN", "US", "DE" }, map.Countries.Select(c => c.CountryCode));
        var us = map.Countries.Single(c => c.CountryCode == "US");
        Assert.Equal(4, us.Visitors);
        Assert.Equal(1, us.Connections);
        Assert.Equal(0, us.Peers);
        Assert.True(us.Server);
        var de = map.Countries.Single(c => c.CountryCode == "DE");
        Assert.Equal(1, de.Failed);
        Assert.Equal(1, de.Peers);
        Assert.False(de.Server);
        Assert.Equal(9, map.Countries.Single(c => c.CountryCode == "CN").Blocked);
    }

    /// <summary>
    /// Every tunnel end is listed with its place, the host in the server's country, and an end with no address unplaced.
    /// True: the WireGuard card and the map read the same ends.
    /// False: an end without an address would be dropped, or the host left unplaced behind a name.
    /// </summary>
    [Fact]
    public void Build_ListsEveryTunnelEndWithItsPlace()
    {
        var map = MapBuilder.Build(false, true, Us, Totals(), Totals(), Totals(), Ends(), Locate);

        Assert.Equal(new[] { "US", "DE", string.Empty, "US" }, map.Tunnels.Select(e => e.CountryCode));
        Assert.Equal(new[] { "Host", "Peer", "Peer", "Connection" }, map.Tunnels.Select(e => e.Kind));
        Assert.Equal("Offline", map.Tunnels[2].State);
    }

    /// <summary>
    /// A tool that is off adds nothing: the proxy's totals with the proxy off, and the tunnels with WireGuard off.
    /// True: the map shows only what a tool that is on gives.
    /// False: visitors would show from a proxy that is off.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Build_ToolOff_AddsNothing(bool proxyOn, bool wireGuardOn)
    {
        var map = MapBuilder.Build(proxyOn, wireGuardOn, Us, Totals(("DE", "Germany", 3)), Totals(), Totals(), Ends(), Locate);

        Assert.Equal(proxyOn ? 3 : 0, map.Countries.Sum(c => c.Visitors));
        Assert.Equal(wireGuardOn ? 1 : 0, map.Countries.Sum(c => c.Peers));
        Assert.Equal(wireGuardOn ? 4 : 0, map.Tunnels.Count);
        Assert.True(map.Countries.Single(c => c.CountryCode == "US").Server);
    }

    /// <summary>
    /// With both tools off, and with no known server place, the map is empty.
    /// True: nothing is drawn for a map the page hides.
    /// False: the server would be marked with no tool on.
    /// </summary>
    [Fact]
    public void Build_BothOffOrNothingKnown_IsEmpty()
    {
        Assert.Empty(MapBuilder.Build(false, false, Us, Totals(("DE", "Germany", 3)), Totals(), Totals(), Ends(), Locate).Countries);
        Assert.Empty(MapBuilder.Build(true, false, GeoLocation.Unknown, Totals(("", "", 3)), Totals(), Totals(), new List<PeerConnection>(), Locate).Countries);
    }

    /// <summary>
    /// A host whose server place is not known yet is placed by its address instead, and never counts as a peer.
    /// True: the host still shows where it is while the server's own lookup runs.
    /// False: the host would add a peer to the map.
    /// </summary>
    [Fact]
    public void Build_HostWithoutServerPlace_IsPlacedByItsAddress()
    {
        var ends = new List<PeerConnection> { new() { Kind = "Host", Name = "Host", RemoteAddress = "203.0.113.9" } };

        var map = MapBuilder.Build(false, true, GeoLocation.Unknown, Totals(), Totals(), Totals(), ends, Locate);

        Assert.Equal("US", Assert.Single(map.Tunnels).CountryCode);
        Assert.Empty(map.Countries);
    }

    /// <summary>
    /// Null inputs are refused.
    /// True: a caller learns at once that it passed nothing.
    /// False: the map would fail later with a null reference.
    /// </summary>
    [Fact]
    public void Build_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MapBuilder.Build(true, true, Us, null!, Totals(), Totals(), Ends(), Locate));
        Assert.Throws<ArgumentNullException>(() => MapBuilder.Build(true, true, Us, Totals(), Totals(), Totals(), Ends(), null!));
    }
}
