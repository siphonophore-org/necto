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
/// Checks every open request against the library, catching titles that arrived while the server was off or
/// before their metadata was known. Library changes are otherwise picked up as they happen.
/// </summary>
public class SweepRequestsTask : PluginScheduledTask
{
    private readonly RequestService _requests;

    /// <summary>
    /// Initializes a new instance of the <see cref="SweepRequestsTask"/> class.
    /// </summary>
    /// <param name="requests">The request service.</param>
    public SweepRequestsTask(RequestService requests)
    {
        _requests = requests;
    }

    /// <inheritdoc />
    public override string Name => ServerWording.Get(TranslationKeys.RequestsTaskSweepName);

    /// <inheritdoc />
    public override string Key => "RequestsSweep";

    /// <inheritdoc />
    public override string Description => ServerWording.Get(TranslationKeys.RequestsTaskSweepDescription);

    /// <inheritdoc />
    public override string Category => ServerWording.Get(TranslationKeys.TabRequests);

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (RequestService.Enabled)
        {
            await _requests.SweepAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return OnStartup();
        yield return Daily(TimeSpan.FromHours(3));
    }
}
