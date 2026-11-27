using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Api.Ddns;
namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// How much location the history shows for an internet address.
/// </summary>
public enum LocationDetail
{
    /// <summary>Country only, from a small database.</summary>
    Country,

    /// <summary>Country and city, from a larger database. Cities are often approximate.</summary>
    City,

    /// <summary>No location lookups and no database download.</summary>
    Off
}
