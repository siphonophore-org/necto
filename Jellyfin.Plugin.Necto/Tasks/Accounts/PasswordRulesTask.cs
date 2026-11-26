using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Accounts;

/// <summary>
/// Drops administrators and deleted users from the groups, and moves every user onto or off password rules, when the
/// server starts.
/// </summary>
public sealed class PasswordRulesTask : PluginScheduledTask
{
    /// <summary>The task's key.</summary>
    public const string TaskKey = "NectoPasswordRules";

    private readonly GroupStore _groups;
    private readonly PasswordEnrollment _enrollment;
    private readonly IServerConfigurationManager _serverConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordRulesTask"/> class.
    /// </summary>
    /// <param name="groups">Drops administrators and deleted users from the groups.</param>
    /// <param name="enrollment">Moves users onto or off password rules.</param>
    /// <param name="serverConfig">Gives the server's language for the task's name.</param>
    public PasswordRulesTask(GroupStore groups, PasswordEnrollment enrollment, IServerConfigurationManager serverConfig)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(serverConfig);
        _groups = groups;
        _enrollment = enrollment;
        _serverConfig = serverConfig;
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.TaskApplyPasswordRules, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.TaskApplyPasswordRulesDescription, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _groups.CleanMembership();
        await _enrollment.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return OnStartup();
    }
}
