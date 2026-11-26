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
/// What the plugin's pages show about the location database.
/// </summary>
public sealed class GeoIpStatus
{
    /// <summary>Gets or sets the configured detail.</summary>
    public LocationDetail Detail { get; set; }

    /// <summary>Gets or sets a value indicating whether a database is loaded.</summary>
    public bool Loaded { get; set; }

    /// <summary>Gets or sets when the loaded database was built (UTC).</summary>
    public DateTime? BuiltUtc { get; set; }

    /// <summary>Gets or sets the last download error, empty when none.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a download is running.</summary>
    public bool Downloading { get; set; }
}
