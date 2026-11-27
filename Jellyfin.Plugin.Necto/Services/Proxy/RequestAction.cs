using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// What the proxy does with a request before routing it.
/// </summary>
public enum RequestAction
{
    /// <summary>Forward it.</summary>
    Forward,

    /// <summary>Close the connection without answering.</summary>
    Drop,

    /// <summary>Answer 403.</summary>
    Forbidden,

    /// <summary>Redirect to HTTPS.</summary>
    Redirect,

    /// <summary>Answer an ACME HTTP-01 challenge.</summary>
    Challenge
}
