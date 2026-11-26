using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// What the People side needs to know about the proxy's settings.
/// </summary>
internal static class ProxyCountryRules
{
    /// <summary>
    /// Whether country rules are on in the given proxy settings.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <returns>True when country rules need the location database.</returns>
    public static bool CountryRulesOn(ProxySettings? settings)
        => settings is not null && settings.Enabled && CountryRules.NeedsDatabase(ProxyOptions.From(settings.Advanced));
}
