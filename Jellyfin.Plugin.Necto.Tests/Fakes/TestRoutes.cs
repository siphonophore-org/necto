using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds proxy routes for tests: each host forwarded to this Jellyfin from the internet, with a Let's Encrypt
/// certificate, as the Jellyfin template makes them.
/// </summary>
public static class TestRoutes
{
    /// <summary>
    /// Builds one Jellyfin route per host, with ids r0, r1 and on.
    /// </summary>
    /// <param name="hosts">The host names.</param>
    /// <returns>The routes.</returns>
    public static List<ProxyRoute> Of(params string[] hosts)
        => hosts.Select((h, i) => new ProxyRoute { Id = "r" + i, Host = h }).ToList();

    /// <summary>
    /// Gives the host names of the routes, as the old single list of names read.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <returns>The hosts, in order.</returns>
    public static List<string> Hosts(ProxySettings settings) => settings.Routes.Select(r => r.Host).ToList();
}
