using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Storage;
using Siphonophore.Stolon;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Necto.Tasks.Storage;

/// <summary>
/// Checks every disk on a schedule, every 30 minutes and at startup by default, so changes reach the activity log
/// without anyone opening Storage.
/// </summary>
public sealed class CheckStorageTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs it by.</summary>
    public const string TaskKey = "NectoStorageCheck";

    private readonly StorageService _storage;
    private readonly IServerConfigurationManager _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckStorageTask"/> class.
    /// </summary>
    /// <param name="storage">Checks the disks.</param>
    /// <param name="config">The server configuration, read for its display language.</param>
    public CheckStorageTask(StorageService storage, IServerConfigurationManager config)
    {
        _storage = storage;
        _config = config;
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.StorageTaskCheckStorage, StolonTranslations.ServerLanguage(_config));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.StorageTaskCheckStorageDescription, StolonTranslations.ServerLanguage(_config));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        // While Storage is off, no disk is checked, mounted again, or alerted on.
        if (_storage.Enabled)
        {
            await _storage.CheckAsync(cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return TaskTriggers.EveryInterval(TimeSpan.FromMinutes(30));
        yield return TaskTriggers.OnStartup();
    }
}
