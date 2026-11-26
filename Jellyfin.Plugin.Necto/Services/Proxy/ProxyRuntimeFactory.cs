using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
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
/// Turns saved settings and Jellyfin's network configuration into a runtime snapshot.
/// </summary>
public static class ProxyRuntimeFactory
{
    /// <summary>
    /// Deep-copies settings, so a snapshot never shares objects with the live configuration.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>An independent copy.</returns>
    public static ProxySettings Clone(ProxySettings? settings)
        => settings is null
            ? new ProxySettings()
            : JsonSerializer.Deserialize<ProxySettings>(JsonSerializer.Serialize(settings)) ?? new ProxySettings();

    /// <summary>The id of the one Jellyfin route a probe runs with.</summary>
    public const string ProbeRouteId = "probe";

    /// <summary>
    /// Turns the enabled routes with a valid host and upstream into what the running proxy forwards by.
    /// </summary>
    /// <param name="settings">The proxy settings.</param>
    /// <param name="jellyfin">Jellyfin's running network setup, for the routes that forward to it.</param>
    /// <returns>The routes.</returns>
    public static IReadOnlyList<RuntimeRoute> RoutesOf(ProxySettings settings, JellyfinNetworkView jellyfin)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jellyfin);
        var routes = new List<RuntimeRoute>();
        foreach (var route in settings.Routes.Where(r => r is { Enabled: true }))
        {
            var host = HostnameMatcher.Normalize(route.Host);
            if (host.Length == 0)
            {
                continue;
            }

            if (route.IsJellyfin)
            {
                routes.Add(new RuntimeRoute(route.Id, host, jellyfin.Backend, true, route.LocalOnly));
            }
            else if (ProxySettingsValidator.ParseUpstream(route.Upstream) is { } upstream)
            {
                routes.Add(new RuntimeRoute(route.Id, host, upstream, false, route.LocalOnly));
            }
        }

        return routes;
    }

    /// <summary>
    /// Builds a runtime snapshot.
    /// </summary>
    /// <param name="settings">The proxy settings (normalized).</param>
    /// <param name="jellyfin">Jellyfin's running network setup.</param>
    /// <param name="isLan">Jellyfin's LAN test.</param>
    /// <param name="httpsAvailable">Whether a certificate is loaded, which decides whether the HTTPS listener runs.</param>
    /// <param name="probe">Whether to build a throwaway probe: HTTP only, on a free loopback port.</param>
    /// <returns>The runtime.</returns>
    public static ProxyRuntime Build(
        ProxySettings settings,
        JellyfinNetworkView jellyfin,
        Func<IPAddress, bool> isLan,
        bool httpsAvailable,
        bool probe = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jellyfin);
        var options = ProxyOptions.From(settings.Advanced);
        var listener = new ListenerOptions(
            probe ? 0 : options.HttpPort,
            probe || !httpsAvailable ? null : options.HttpsPort,
            probe,
            options.EnableHttp2,
            options.KeepAliveSeconds,
            options.RequestHeadersTimeoutSeconds,
            options.MaxRequestHeadersKb,
            options.MaxConnections);

        return new ProxyRuntime
        {
            Listener = listener,
            Routes = probe ? new[] { new RuntimeRoute(ProbeRouteId, "*.invalid", jellyfin.Backend, true, true) } : RoutesOf(settings, jellyfin),
            JellyfinBackend = jellyfin.Backend,
            BaseUrl = jellyfin.BaseUrl,
            JellyfinTrustsForwarding = jellyfin.TrustsForwarding,
            Options = options,
            Classifier = new NetworkClassifier(isLan, NetworkClassifier.ParseNetworks(options.TrustedNetworks)),
            BlockedNetworks = NetworkClassifier.ParseNetworks(options.BlockedAddresses)
        };
    }
}
