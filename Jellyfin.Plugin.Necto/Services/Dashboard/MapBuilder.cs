using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Api.WireGuard;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// Joins the proxy's visitor totals, the WireGuard tunnel ends, and this server's place into the Dashboard map.
/// </summary>
public static class MapBuilder
{
    /// <summary>
    /// Builds the map. The proxy's layers count only while the proxy is on, and the tunnel layers only while
    /// WireGuard is on. The host end is this server, so it marks the server's country rather than adding a count.
    /// </summary>
    /// <param name="proxyOn">Whether the proxy is on.</param>
    /// <param name="wireGuardOn">Whether WireGuard is on.</param>
    /// <param name="server">Where this server is.</param>
    /// <param name="visitors">The people the proxy saw, by country.</param>
    /// <param name="failed">The failed sign-ins, by country.</param>
    /// <param name="blocked">The refused connections, by country.</param>
    /// <param name="ends">The WireGuard tunnel ends.</param>
    /// <param name="locate">Places a public address, or gives an unknown place for one it cannot.</param>
    /// <returns>The map, its countries most counted first.</returns>
    public static DashboardMap Build(
        bool proxyOn,
        bool wireGuardOn,
        GeoLocation server,
        IReadOnlyList<CountryTotal> visitors,
        IReadOnlyList<CountryTotal> failed,
        IReadOnlyList<CountryTotal> blocked,
        IReadOnlyList<PeerConnection> ends,
        Func<string?, GeoLocation> locate)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(visitors);
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(blocked);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(locate);

        var map = new DashboardMap { Proxy = proxyOn, WireGuard = wireGuardOn, Server = server };
        var countries = new Dictionary<string, MapCountry>(StringComparer.Ordinal);

        MapCountry? At(string code, string name)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            if (!countries.TryGetValue(code, out var country))
            {
                country = new MapCountry { CountryCode = code, Country = name ?? string.Empty };
                countries[code] = country;
            }
            else if (country.Country.Length == 0 && !string.IsNullOrEmpty(name))
            {
                country.Country = name;
            }

            return country;
        }

        if (proxyOn)
        {
            foreach (var total in visitors.Where(t => t.Count > 0))
            {
                if (At(total.CountryCode, total.Country) is { } country)
                {
                    country.Visitors += total.Count;
                }
            }

            foreach (var total in failed.Where(t => t.Count > 0))
            {
                if (At(total.CountryCode, total.Country) is { } country)
                {
                    country.Failed += total.Count;
                }
            }

            foreach (var total in blocked.Where(t => t.Count > 0))
            {
                if (At(total.CountryCode, total.Country) is { } country)
                {
                    country.Blocked += total.Count;
                }
            }
        }

        if (wireGuardOn)
        {
            foreach (var end in ends)
            {
                var host = string.Equals(end.Kind, "Host", StringComparison.Ordinal);
                // The host is this server, so it is wherever the server is, even behind a name.
                var place = host && server.CountryCode.Length > 0 ? server : locate(end.RemoteAddress) ?? GeoLocation.Unknown;
                map.Tunnels.Add(new MapTunnelEnd
                {
                    Kind = end.Kind,
                    Name = end.Name,
                    State = end.State,
                    Connected = end.Connected,
                    CountryCode = place.CountryCode,
                    Country = place.Country
                });

                if (host || At(place.CountryCode, place.Country) is not { } country)
                {
                    continue;
                }

                if (string.Equals(end.Kind, "Connection", StringComparison.Ordinal))
                {
                    country.Connections++;
                }
                else
                {
                    country.Peers++;
                }
            }
        }

        if ((proxyOn || wireGuardOn) && At(server.CountryCode, server.Country) is { } home)
        {
            home.Server = true;
        }

        map.Countries = countries.Values
            .OrderByDescending(c => c.Visitors + c.Failed + c.Blocked + c.Peers + c.Connections)
            .ThenBy(c => c.Country, StringComparer.Ordinal)
            .ThenBy(c => c.CountryCode, StringComparer.Ordinal)
            .ToList();
        return map;
    }
}
