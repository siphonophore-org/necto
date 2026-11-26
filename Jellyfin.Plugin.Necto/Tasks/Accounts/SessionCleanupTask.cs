using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Accounts;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Accounts;

/// <summary>
/// Logs out stale devices by the session rules of their users' groups, once a day.
/// </summary>
public sealed class SessionCleanupTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs it by.</summary>
    public const string TaskKey = "NectoSessionCleanup";

    private readonly SessionCleanupService _sessions;
    private readonly IServerConfigurationManager _serverConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionCleanupTask"/> class.
    /// </summary>
    /// <param name="sessions">Logs out stale devices.</param>
    /// <param name="serverConfig">Gives the server's language for the task's name.</param>
    public SessionCleanupTask(SessionCleanupService sessions, IServerConfigurationManager serverConfig)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(serverConfig);
        _sessions = sessions;
        _serverConfig = serverConfig;
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.TaskCleanSessions, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.TaskCleanSessionsDescription, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _sessions.CleanupAsync(progress, cancellationToken);

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.Zero);
    }
}
