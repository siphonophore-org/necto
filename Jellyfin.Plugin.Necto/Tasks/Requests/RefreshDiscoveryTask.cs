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
/// Stores the request page's rows from TMDb, and what Radarr and Sonarr have, in the server's cache folder, daily, so
/// the page reads them from there rather than from TMDb, Radarr and Sonarr, and still has them after a restart or
/// while any of them is down.
/// </summary>
public class RefreshDiscoveryTask : PluginScheduledTask
{
    private readonly SearchService _search;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshDiscoveryTask"/> class.
    /// </summary>
    /// <param name="search">The search service, which reads the rows.</param>
    public RefreshDiscoveryTask(SearchService search)
    {
        _search = search;
    }

    /// <inheritdoc />
    public override string Name => ServerWording.Get(TranslationKeys.RequestsTaskDiscoveryName);

    /// <inheritdoc />
    public override string Key => "RequestsRefreshDiscovery";

    /// <inheritdoc />
    public override string Description => ServerWording.Get(TranslationKeys.RequestsTaskDiscoveryDescription);

    /// <inheritdoc />
    public override string Category => ServerWording.Get(TranslationKeys.TabRequests);

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        // TMDb not answering in full leaves the copy before in place; the task says it failed, so it shows.
        if (await _search.RefreshStoredAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException("TMDb could not be read in full, or there is no TMDb key to read it with, so the stored rows were kept as they were.");
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.Zero);
    }
}
