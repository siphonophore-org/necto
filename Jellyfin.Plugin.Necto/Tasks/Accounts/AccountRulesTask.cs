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
/// Disables or deletes users whose groups have expired, and disables inactive users, once a day. The run's time is the
/// time accounts expire.
/// </summary>
public sealed class AccountRulesTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs it by.</summary>
    public const string TaskKey = "NectoAccountRules";

    private readonly AccountRuleService _rules;
    private readonly IServerConfigurationManager _serverConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountRulesTask"/> class.
    /// </summary>
    /// <param name="rules">Applies expiry and inactivity.</param>
    /// <param name="serverConfig">Gives the server's language for the task's name.</param>
    public AccountRulesTask(AccountRuleService rules, IServerConfigurationManager serverConfig)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(serverConfig);
        _rules = rules;
        _serverConfig = serverConfig;
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.TaskProcessExpired, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.TaskProcessExpiredDescription, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _rules.ProcessAsync(progress, cancellationToken);

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.Zero);
    }
}
