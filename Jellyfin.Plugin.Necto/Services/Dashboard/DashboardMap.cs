using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// What the Dashboard's world map draws: the proxy's visitors and the WireGuard tunnel ends by country, and this
/// server's own place.
/// </summary>
public sealed class DashboardMap
{
    /// <summary>Gets or sets a value indicating whether the proxy is on, so its layers show.</summary>
    public bool Proxy { get; set; }

    /// <summary>Gets or sets a value indicating whether WireGuard is on, so its layers show.</summary>
    public bool WireGuard { get; set; }

    /// <summary>Gets or sets where this server is, with empty fields while it is not known.</summary>
    public GeoLocation Server { get; set; } = GeoLocation.Unknown;

    /// <summary>Gets or sets the counts of each country that has any, most first.</summary>
    public List<MapCountry> Countries { get; set; } = new();

    /// <summary>Gets or sets every WireGuard tunnel end with the country it is in, while WireGuard is on.</summary>
    public List<MapTunnelEnd> Tunnels { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the location database is loaded, without which nothing is placed.</summary>
    public bool LocationLoaded { get; set; }

    /// <summary>Gets or sets the attribution the location database's licence asks for wherever its data shows.</summary>
    public string Attribution { get; set; } = GeoIpService.Attribution;
}
