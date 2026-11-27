using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// Feeds YARP its routes and clusters from the runtime snapshot, and pushes changes live.
/// </summary>
/// <remarks>
/// Routes are selected by an internal header the policy middleware sets, not by YARP's own host matching,
/// so media and everything else reach Jellyfin through the cluster the policy picked.
/// </remarks>
public sealed class ProxyConfigProvider : IProxyConfigProvider
{
    /// <summary>How long a media stream may sit idle, long enough for a paused film.</summary>
    public static readonly TimeSpan MediaActivityTimeout = TimeSpan.FromHours(6);

    /// <summary>
    /// The most connections each Jellyfin cluster opens to Jellyfin. YARP otherwise opens one per request in
    /// flight without limit. This is far above what a household uses, open sessions and streams included, and
    /// requests past it wait for a free connection rather than fail.
    /// </summary>
    public const int JellyfinConnections = 256;

    private volatile Snapshot _snapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyConfigProvider"/> class.
    /// </summary>
    /// <param name="runtime">The initial runtime.</param>
    public ProxyConfigProvider(ProxyRuntime runtime)
    {
        _snapshot = Create(runtime);
    }

    /// <inheritdoc />
    public IProxyConfig GetConfig() => _snapshot;

    /// <summary>
    /// Replaces the routes and clusters and tells YARP to reload.
    /// </summary>
    /// <param name="runtime">The new runtime.</param>
    public void Update(ProxyRuntime runtime)
    {
        var old = _snapshot;
        _snapshot = Create(runtime);
        old.SignalChange();
    }

    /// <summary>
    /// Builds the YARP routes for a runtime.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The routes.</returns>
    public static IReadOnlyList<RouteConfig> BuildRoutes(ProxyRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var routes = new List<RouteConfig>();
        foreach (var route in runtime.Routes)
        {
            routes.Add(Route(route.Id, route.Options));
            if (route.IsJellyfin)
            {
                routes.Add(Route(route.Id + ProxyRuntime.MediaSuffix));
            }
        }

        return routes;
    }

    /// <summary>
    /// Builds the YARP clusters for a runtime.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The clusters.</returns>
    public static IReadOnlyList<ClusterConfig> BuildClusters(ProxyRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var request = new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromSeconds(EffectiveSettings.BackendActivityTimeoutSeconds(runtime)),
            AllowResponseBuffering = false,
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        var clusters = new List<ClusterConfig>();
        foreach (var route in runtime.Routes)
        {
            // A route to another service may buffer, and may wait longer or shorter than Settings says.
            var own = route.Options is { } o
                ? request with
                {
                    AllowResponseBuffering = o.Buffering,
                    ActivityTimeout = o.TimeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : request.ActivityTimeout
                }
                : request;
            clusters.Add(Cluster(route.Id, route.Backend, own));

            // YARP's activity timer only restarts when a read or write completes, and a paused player stops
            // reading, which stalls the write. Media gets a long timer so a pause does not end the stream.
            if (route.IsJellyfin)
            {
                clusters.Add(Cluster(route.Id + ProxyRuntime.MediaSuffix, route.Backend, request with { ActivityTimeout = MediaActivityTimeout }));
            }
        }

        return clusters;
    }

    private static ClusterConfig Cluster(string id, Uri backend, ForwarderRequestConfig request) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
        {
            [id] = new() { Address = backend.ToString() }
        },
        HttpRequest = request,
        HttpClient = new HttpClientConfig { MaxConnectionsPerServer = JellyfinConnections }
    };

    /// <summary>The route metadata key that says whether the backend sees the visitor's host name.</summary>
    public const string PreserveHostKey = "necto.preserveHost";

    private static RouteConfig Route(string id, RouteOptions? options = null) => new()
    {
        RouteId = id,
        ClusterId = id,
        Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [PreserveHostKey] = options?.PreserveHost == false ? "false" : "true" },
        Transforms = Transforms(options),
        Match = new RouteMatch
        {
            Path = "{**catch-all}",
            Headers = new[]
            {
                new RouteHeader { Name = ProxyPolicyMiddleware.RouteHeader, Values = new[] { id }, Mode = HeaderMatchMode.ExactHeader, IsCaseSensitive = true }
            }
        }
    };

    // The path prefix and the headers a route to another service sets, as YARP's own transforms.
    private static List<IReadOnlyDictionary<string, string>>? Transforms(RouteOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var list = new List<IReadOnlyDictionary<string, string>>();
        if (options.PathPrefix.Length > 0)
        {
            list.Add(new Dictionary<string, string> { ["PathPrefix"] = options.PathPrefix });
        }

        foreach (var header in options.RequestHeaders)
        {
            list.Add(new Dictionary<string, string> { ["RequestHeader"] = header.Name, ["Set"] = header.Value });
        }

        foreach (var header in options.ResponseHeaders)
        {
            list.Add(new Dictionary<string, string> { ["ResponseHeader"] = header.Name, ["Set"] = header.Value, ["When"] = "Always" });
        }

        return list;
    }

    private static Snapshot Create(ProxyRuntime runtime) => new(BuildRoutes(runtime), BuildClusters(runtime));

    private sealed class Snapshot : IProxyConfig, IDisposable
    {
        private readonly CancellationTokenSource _changed = new();

        public Snapshot(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_changed.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }

        public IReadOnlyList<ClusterConfig> Clusters { get; }

        public IChangeToken ChangeToken { get; }

        public void SignalChange()
        {
            _changed.Cancel();
            Dispose();
        }

        public void Dispose() => _changed.Dispose();
    }
}
