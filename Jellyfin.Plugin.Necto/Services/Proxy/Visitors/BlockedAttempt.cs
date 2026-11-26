using System;
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
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

/// <summary>
/// An internet address the block list or country rules refused, with how often and when.
/// </summary>
public sealed class BlockedAttempt
{
    /// <summary>Gets or sets the address.</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Gets or sets the two-letter country code, empty when unknown.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the country name, empty when unknown.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Gets or sets what refused it.</summary>
    public BlockReason Reason { get; set; }

    /// <summary>Gets or sets how many connections were refused.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the first refusal (UTC).</summary>
    public DateTime FirstUtc { get; set; }

    /// <summary>Gets or sets the latest refusal (UTC).</summary>
    public DateTime LastUtc { get; set; }
}
