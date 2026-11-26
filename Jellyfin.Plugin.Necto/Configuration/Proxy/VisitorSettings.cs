using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// Settings for the history: who connected from where.
/// </summary>
public class VisitorSettings
{
    /// <summary>Gets or sets a value indicating whether sign-ins and connections are recorded.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets how many days of history are kept.</summary>
    public int HistoryDays { get; set; } = 90;

    /// <summary>Gets or sets how much location is looked up.</summary>
    public LocationDetail Location { get; set; } = LocationDetail.Country;
}
