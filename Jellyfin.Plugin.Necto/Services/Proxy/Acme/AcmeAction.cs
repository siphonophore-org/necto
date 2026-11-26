using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
namespace Jellyfin.Plugin.Necto.Services.Proxy.Acme;

/// <summary>
/// What the certificate planner wants done.
/// </summary>
public enum AcmeAction
{
    /// <summary>The certificate is current; nothing to do.</summary>
    None,

    /// <summary>There is no certificate for these names yet; get one.</summary>
    Issue,

    /// <summary>The certificate is close to expiring; get a new one.</summary>
    Renew,

    /// <summary>Recent attempts failed; wait before trying again.</summary>
    Wait,

    /// <summary>There is no name Let's Encrypt can check over HTTP-01, such as only wildcards.</summary>
    NoNames
}
