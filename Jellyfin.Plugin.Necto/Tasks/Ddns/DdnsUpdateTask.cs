using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;

namespace Jellyfin.Plugin.Necto.Tasks.Ddns;

/// <summary>
/// The recurring task that checks the public IP and updates records on change.
/// </summary>
public sealed class DdnsUpdateTask : PluginScheduledTask
{
    private readonly DNSUpdateService _updateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DdnsUpdateTask"/> class.
    /// </summary>
    /// <param name="updateService">The update service.</param>
    public DdnsUpdateTask(DNSUpdateService updateService)
    {
        _updateService = updateService;
    }

    /// <inheritdoc />
    public override string Name => ServerText.Get(TranslationKeys.DdnsTaskUpdate);

    /// <inheritdoc />
    public override string Key => "NectoDdnsUpdate";

    /// <inheritdoc />
    public override string Description => ServerText.Get(TranslationKeys.DdnsTaskUpdateDdnsDescription);

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(0);
        await _updateService.RunAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Seeds the default schedule only. Administrators change these under Scheduled Tasks.
        yield return EveryInterval(TimeSpan.FromMinutes(DdnsLimits.MinUpdateEveryMinutes));
        yield return OnStartup();
    }
}
