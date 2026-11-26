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
/// How much came from one country, for the Dashboard map.
/// </summary>
public sealed class CountryTotal
{
    /// <summary>Gets or sets the two-letter country code.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Gets or sets the country name.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Gets or sets the count: people seen, or failed sign-ins.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets when the latest one happened (UTC).</summary>
    public DateTime LastUtc { get; set; }
}
