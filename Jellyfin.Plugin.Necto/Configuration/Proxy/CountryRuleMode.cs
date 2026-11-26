using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// How the proxy treats internet connections by country.
/// </summary>
public enum CountryRuleMode
{
    /// <summary>No country rules.</summary>
    Off,

    /// <summary>Refuse connections from every country except the one this server is in and any listed.</summary>
    ServerCountry,

    /// <summary>Refuse connections from the listed countries.</summary>
    Block,

    /// <summary>Refuse connections from every country not listed.</summary>
    AllowOnly
}
