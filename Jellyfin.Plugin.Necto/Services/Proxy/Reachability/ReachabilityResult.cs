using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Reachability;

/// <summary>
/// The last check of whether this server's HTTPS port can be reached from outside the network.
/// </summary>
public sealed class ReachabilityResult
{
    /// <summary>Gets or sets when the check ran (UTC), null before any check.</summary>
    public DateTime? CheckedUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether at least one outside location connected. Null before any check,
    /// and when the check itself could not run.
    /// </summary>
    public bool? Reachable { get; set; }

    /// <summary>Gets or sets what went wrong, empty when nothing did.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Gets or sets the outside locations that tried to connect.</summary>
    public List<ReachabilityNode> Nodes { get; set; } = new();

    /// <summary>
    /// Copies the result, so a caller never holds the stored one.
    /// </summary>
    /// <returns>The copy.</returns>
    public ReachabilityResult Copy()
    {
        var copy = (ReachabilityResult)MemberwiseClone();
        copy.Nodes = Nodes.Select(n => new ReachabilityNode { Location = n.Location, Ok = n.Ok, Error = n.Error }).ToList();
        return copy;
    }
}

/// <summary>
/// One outside location's attempt to connect.
/// </summary>
public sealed class ReachabilityNode
{
    /// <summary>Gets or sets where the attempt came from, as "City, Country".</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it connected.</summary>
    public bool Ok { get; set; }

    /// <summary>Gets or sets why it did not connect, empty when it did.</summary>
    public string Error { get; set; } = string.Empty;
}
