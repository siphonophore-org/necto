using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Hosting;

/// <summary>
/// What the plugin hands the proxy host. Deliberately small: the host gets the runtime, the certificate,
/// the challenges and a logger, and nothing from Jellyfin's own service container.
/// </summary>
public sealed class ProxyHostOptions
{
    /// <summary>Gets the runtime holder.</summary>
    public required ProxyRuntimeHolder Holder { get; init; }

    /// <summary>Gets the certificate store.</summary>
    public required CertificateSet Certificates { get; init; }

    /// <summary>Gets the pending HTTP-01 challenges.</summary>
    public required Http01ChallengeStore Challenges { get; init; }

    /// <summary>Gets Jellyfin's logger factory.</summary>
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>Gets the content root for the host (the proxy data directory).</summary>
    public required string ContentRoot { get; init; }

    /// <summary>Gets where the proxy reports which address each client used, if anywhere.</summary>
    public IAccessRecorder? AccessRecorder { get; init; }

    /// <summary>Gets where failed sign-ins from the internet are reported, to ban repeat offenders.</summary>
    public IFailureTracker? FailureTracker { get; init; }

    /// <summary>Gets the connection filters: the block list, bans and country rules.</summary>
    public IEnumerable<IConnectionFilter> ConnectionFilters { get; init; } = new List<IConnectionFilter>();

    /// <summary>Gets what notices every visitor looking local.</summary>
    public LocalTrafficWatch LocalTraffic { get; init; } = new();
}
