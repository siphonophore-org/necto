using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.CustomPages;

/// <summary>
/// Applies each store's retention on a schedule. A read or write already drops expired records, so this is for the
/// stores nobody touches, which would otherwise keep their last records past their retention.
/// </summary>
public sealed class StoreRetentionTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs.</summary>
    public const string TaskKey = "NectoPagesStoreRetention";

    private readonly IStoreService _stores;
    private readonly ILogger<StoreRetentionTask> _logger;
    private readonly IServerConfigurationManager _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreRetentionTask"/> class.
    /// </summary>
    /// <param name="stores">Holds the records.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="config">Gives the server's language.</param>
    public StoreRetentionTask(IStoreService stores, ILogger<StoreRetentionTask> logger, IServerConfigurationManager config)
    {
        _stores = stores ?? throw new ArgumentNullException(nameof(stores));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.PagesTaskRetentionName, StolonTranslations.ServerLanguage(_config));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description
        => StolonTranslations.Get(TranslationKeys.PagesTaskRetentionDescription, StolonTranslations.ServerLanguage(_config));

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        // A cancelled run stops between stores and surfaces as OperationCanceledException.
        var removed = _stores.SweepAll(cancellationToken);
        if (removed > 0)
        {
            _logger.LogInformation("Pages retention removed {Count} expired store records", removed);
        }

        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Retention is in days, so every six hours is far finer than the rule and keeps the task cheap.
        yield return TaskTriggers.EveryInterval(TimeSpan.FromHours(6));
    }
}
