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
/// Rejects requests left waiting too long, then removes rejected and available requests older than the retention setting,
/// and closed issues older than theirs. Open issues whose file changed or went while nothing watched are marked.
/// </summary>
public class CleanupRequestsTask : PluginScheduledTask
{
    private readonly RequestService _requests;
    private readonly ArtworkCache _artwork;
    private readonly IssueService _issues;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupRequestsTask"/> class.
    /// </summary>
    /// <param name="requests">The request service.</param>
    /// <param name="artwork">The TMDb images kept in the cache folder.</param>
    /// <param name="issues">The issue service.</param>
    public CleanupRequestsTask(RequestService requests, ArtworkCache artwork, IssueService issues)
    {
        _requests = requests;
        _artwork = artwork;
        _issues = issues;
    }

    /// <inheritdoc />
    public override string Name => ServerWording.Get(TranslationKeys.RequestsTaskCleanupName);

    /// <inheritdoc />
    public override string Key => "RequestsCleanup";

    /// <inheritdoc />
    public override string Description => ServerWording.Get(TranslationKeys.RequestsTaskCleanupDescription);

    /// <inheritdoc />
    public override string Category => ServerWording.Get(TranslationKeys.TabRequests);

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _requests.Cleanup();
        progress?.Report(40);
        _issues.Clean();
        _issues.CheckFiles();
        progress?.Report(70);
        _artwork.Trim();
        progress?.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.FromHours(4));
    }
}
