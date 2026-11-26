using System.Net;
using Jellyfin.Plugin.Necto.Configuration;
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
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// A failure tracker that bans nothing, for proxies started without one (probes and tests).
/// </summary>
public sealed class NullFailureTracker : IFailureTracker
{
    /// <summary>Gets the shared instance.</summary>
    public static IFailureTracker Instance { get; } = new NullFailureTracker();

    /// <inheritdoc />
    public bool RecordFailure(IPAddress client, ProxyOptions options) => false;
}
