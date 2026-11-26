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
/// What an address did, for the Remote Access table.
/// </summary>
public enum ActivityType
{
    /// <summary>A user signed in or had a session from it.</summary>
    User,

    /// <summary>A sign-in from it failed.</summary>
    Failure,

    /// <summary>The block list or the country rules refused it.</summary>
    Blocked,

    /// <summary>It reached the page from the internet but never tried to sign in.</summary>
    Seen
}
