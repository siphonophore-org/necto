using System.Net;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// Where the proxy reports failed sign-ins from the internet, so repeated failures can ban the address.
/// </summary>
public interface IFailureTracker
{
    /// <summary>
    /// Notes a failed sign-in from an internet address.
    /// </summary>
    /// <param name="client">The normalized client address.</param>
    /// <param name="options">The options in force, holding the ban limits.</param>
    /// <returns>
    /// True when this failure newly banned the client, so its connections should be closed. An address already
    /// banned gives false: its connections were closed when the ban landed.
    /// </returns>
    bool RecordFailure(IPAddress client, ProxyOptions options);
}
