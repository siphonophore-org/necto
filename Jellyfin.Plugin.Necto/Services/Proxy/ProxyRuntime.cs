using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// An immutable snapshot of everything the running proxy needs. Settings changes build a new snapshot and
/// swap it in, so a request always sees one consistent configuration.
/// </summary>
public sealed class ProxyRuntime
{
    /// <summary>The suffix of the YARP route and cluster id for a Jellyfin route's media streams, which a paused player may leave idle.</summary>
    public const string MediaSuffix = "-media";

    /// <summary>Gets the listener options.</summary>
    public required ListenerOptions Listener { get; init; }

    /// <summary>Gets the enabled routes, each a host name and the service it forwards to.</summary>
    public required IReadOnlyList<RuntimeRoute> Routes { get; init; }

    /// <summary>Gets Jellyfin's address on this machine.</summary>
    public required Uri JellyfinBackend { get; init; }

    /// <summary>Gets Jellyfin's base URL, empty or like <c>/jellyfin</c>.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Gets a value indicating whether Jellyfin honours the proxy's forwarding headers. When it does not,
    /// every proxied client looks local to Jellyfin, so internet requests for Jellyfin are refused.
    /// </summary>
    public required bool JellyfinTrustsForwarding { get; init; }

    /// <summary>Gets the resolved options: the administrator's changes over the built-in defaults.</summary>
    public required ProxyOptions Options { get; init; }

    /// <summary>Gets the LAN classifier.</summary>
    public required NetworkClassifier Classifier { get; init; }

    /// <summary>Gets the internet addresses that are always refused.</summary>
    public IReadOnlyList<IPNetwork> BlockedNetworks { get; init; } = Array.Empty<IPNetwork>();

    /// <summary>
    /// Whether a host is one the proxy was configured to serve.
    /// </summary>
    /// <param name="host">The request host, without port.</param>
    /// <returns>True for configured hostnames.</returns>
    public bool IsConfiguredHost(string? host) => RouteFor(host) is not null;

    /// <summary>
    /// Finds the route that answers a host. An exact name beats a wildcard, so <c>media.example.com</c> wins over
    /// <c>*.example.com</c>.
    /// </summary>
    /// <param name="host">The request host, without port.</param>
    /// <returns>The route, or <c>null</c> when none answers it.</returns>
    public RuntimeRoute? RouteFor(string? host)
        => Routes.Where(r => HostnameMatcher.Matches(host, r.Host))
            .OrderBy(r => r.Host.StartsWith('*') ? 1 : 0)
            .FirstOrDefault();

    /// <summary>
    /// Finds the route a local client reaches. A local client that asks for no route's name, such as by this
    /// server's address, reaches the first Jellyfin route.
    /// </summary>
    /// <param name="host">The request host, without port.</param>
    /// <returns>The route, or <c>null</c> when none answers it.</returns>
    public RuntimeRoute? RouteForLocal(string? host) => RouteFor(host) ?? Routes.FirstOrDefault(r => r.IsJellyfin);
}
