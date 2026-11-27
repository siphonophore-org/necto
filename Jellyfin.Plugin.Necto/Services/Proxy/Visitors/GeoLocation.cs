using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Api.Ddns;
namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// Where an address is, as far as the location database knows.
/// </summary>
/// <param name="CountryCode">The two-letter country code, empty when unknown.</param>
/// <param name="Country">The country name, empty when unknown.</param>
/// <param name="City">The city name, empty when unknown or not looked up.</param>
public sealed record GeoLocation(string CountryCode, string Country, string City)
{
    /// <summary>Gets an unknown location.</summary>
    public static GeoLocation Unknown { get; } = new(string.Empty, string.Empty, string.Empty);
}
