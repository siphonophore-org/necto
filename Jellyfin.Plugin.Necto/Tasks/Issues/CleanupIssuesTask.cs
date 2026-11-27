using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using MediaBrowser.Model.Tasks;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Issues;

/// <summary>
/// Removes closed issues older than their retention setting, and marks open issues whose file changed or went while
/// nothing watched.
/// </summary>
public class CleanupIssuesTask : PluginScheduledTask
{
    private readonly IssueService _issues;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupIssuesTask"/> class.
    /// </summary>
    /// <param name="issues">The issue service.</param>
    public CleanupIssuesTask(IssueService issues)
    {
        _issues = issues;
    }

    /// <inheritdoc />
    public override string Name => ServerWording.Get(TranslationKeys.IssuesTaskCleanupName);

    /// <inheritdoc />
    public override string Key => "IssuesCleanup";

    /// <inheritdoc />
    public override string Description => ServerWording.Get(TranslationKeys.IssuesHelpCleanupTask);

    /// <inheritdoc />
    public override string Category => ServerWording.Get(TranslationKeys.TabIssues);

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (IssueService.Enabled)
        {
            _issues.Clean();
            progress?.Report(50);
            _issues.CheckFiles();
        }

        progress?.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.FromHours(4));
    }
}
