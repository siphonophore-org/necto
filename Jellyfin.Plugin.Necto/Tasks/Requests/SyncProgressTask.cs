using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Tasks.Requests;

/// <summary>
/// Reads back download progress from Radarr and Sonarr for approved requests, retries requests they refused, and
/// reminds administrators about requests left waiting. Hourly, so a reminder held for an administrator's next device
/// and the once-a-day expiring reminders go out close to when they're due.
/// </summary>
public class SyncProgressTask : PluginScheduledTask
{
    private readonly RequestService _requests;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncProgressTask"/> class.
    /// </summary>
    /// <param name="requests">The request service.</param>
    public SyncProgressTask(RequestService requests)
    {
        _requests = requests;
    }

    /// <inheritdoc />
    public override string Name => ServerWording.Get(TranslationKeys.RequestsTaskSyncName);

    /// <inheritdoc />
    public override string Key => "RequestsSyncProgress";

    /// <inheritdoc />
    public override string Description => ServerWording.Get(TranslationKeys.RequestsTaskSyncDescription);

    /// <inheritdoc />
    public override string Category => ServerWording.Get(TranslationKeys.TabRequests);

    // Runs every hour; logging each run would bury the task log.
    /// <inheritdoc />
    public override bool IsLogged => false;

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Reminders first: they need only the database, so Radarr or Sonarr being unreachable doesn't hold them up.
        _requests.Remind(DateTime.UtcNow);
        _requests.AutoDecline(DateTime.UtcNow);
        return SyncAsync(progress, cancellationToken);
    }

    // Progress from Radarr and Sonarr, then the genres of older requests, which filtering by genre needs.
    private async Task SyncAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _requests.SyncProgressAsync(progress, cancellationToken).ConfigureAwait(false);
        await _requests.FillGenresAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return EveryInterval(TimeSpan.FromHours(1));
    }
}
