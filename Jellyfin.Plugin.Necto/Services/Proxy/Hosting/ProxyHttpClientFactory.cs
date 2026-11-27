using System;
using System.Net.Http;
using Yarp.ReverseProxy.Forwarder;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// Builds the HTTP clients YARP forwards to Jellyfin with, with a short connect timeout.
/// </summary>
public sealed class ProxyHttpClientFactory : ForwarderHttpClientFactory
{
    /// <inheritdoc />
    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handler);
        base.ConfigureHandler(context, handler);
        handler.ConnectTimeout = TimeSpan.FromSeconds(10);
    }
}
