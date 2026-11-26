using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
namespace Jellyfin.Plugin.Necto.Services.Proxy.Network;

/// <summary>
/// Where the takeover of Jellyfin's ports stands.
/// </summary>
public enum TakeoverPhase
{
    /// <summary>Jellyfin's network settings are its own.</summary>
    Unmanaged,

    /// <summary>New settings are written and Jellyfin is restarting to pick them up.</summary>
    PendingRestart,

    /// <summary>Jellyfin runs behind the proxy and the last startup check passed.</summary>
    Managed,

    /// <summary>
    /// Written by older versions after an automatic rollback, which no longer happens. Kept so their state
    /// files still load; it is treated like <see cref="Unmanaged"/>.
    /// </summary>
    Degraded
}
