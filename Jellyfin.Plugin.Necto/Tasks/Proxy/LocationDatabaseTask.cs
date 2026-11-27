using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Tasks.Proxy;

/// <summary>
/// Downloads the newest location database for the Dashboard's map and the country rules. DB-IP publishes a new one each month.
/// </summary>
public sealed class LocationDatabaseTask : PluginScheduledTask
{
    private readonly GeoIpService _geo;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocationDatabaseTask"/> class.
    /// </summary>
    /// <param name="geo">The location lookup.</param>
    public LocationDatabaseTask(GeoIpService geo)
    {
        _geo = geo;
    }

    /// <inheritdoc />
    public override string Name => ServerText.Get(TranslationKeys.ProxyTaskRefreshLocations);

    /// <inheritdoc />
    public override string Key => "NectoProxyLocations";

    /// <inheritdoc />
    public override string Description => ServerText.Get(TranslationKeys.ProxyTaskRefreshLocationsDescription);

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(0);

        // Nothing needs locations while history and country rules are off, so nothing is downloaded.
        var detail = VisitorService.RequiredLocation;
        if (detail != LocationDetail.Off)
        {
            await _geo.UpdateAsync(detail, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Weekly is plenty for a monthly database; a missed month is retried the next week.
        yield return EveryInterval(TimeSpan.FromDays(7));
    }
}
