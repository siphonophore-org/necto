using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Policies;

/// <summary>
/// Writes each user's group template onto their Jellyfin policy and preferences, which repairs a change made in the
/// dashboard. It runs when the server starts.
/// </summary>
public sealed class PolicyTemplatesTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs it by.</summary>
    public const string TaskKey = "NectoPolicyTemplates";

    private readonly PolicyTemplateService _templates;
    private readonly IServerConfigurationManager _serverConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyTemplatesTask"/> class.
    /// </summary>
    /// <param name="templates">Writes the templates.</param>
    /// <param name="serverConfig">Gives the server's language for the task's name.</param>
    public PolicyTemplatesTask(PolicyTemplateService templates, IServerConfigurationManager serverConfig)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.GroupsTaskApplyTemplates, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.GroupsTaskApplyTemplatesDescription, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _templates.ApplyAllAsync(progress, cancellationToken);

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return OnStartup();
        yield return Daily(TimeSpan.Zero);
    }
}
