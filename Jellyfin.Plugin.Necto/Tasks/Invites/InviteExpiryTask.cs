using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Invites;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Invites;

/// <summary>
/// Turns off every invite whose day has come, once a day. An invite also stops working on its day before the task runs.
/// </summary>
public sealed class InviteExpiryTask : PluginScheduledTask
{
    /// <summary>The task's key, which Troubleshooting runs it by.</summary>
    public const string TaskKey = "NectoInviteExpiry";

    private readonly InviteService _invites;
    private readonly IServerConfigurationManager _serverConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteExpiryTask"/> class.
    /// </summary>
    /// <param name="invites">Turns the invites off.</param>
    /// <param name="serverConfig">Gives the server's language for the task's name.</param>
    public InviteExpiryTask(InviteService invites, IServerConfigurationManager serverConfig)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
    }

    /// <inheritdoc />
    public override string Name => StolonTranslations.Get(TranslationKeys.InvitesTaskExpire, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Key => TaskKey;

    /// <inheritdoc />
    public override string Description => StolonTranslations.Get(TranslationKeys.InvitesTaskExpireDescription, StolonTranslations.ServerLanguage(_serverConfig));

    /// <inheritdoc />
    public override string Category => "Necto";

    /// <inheritdoc />
    public override Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _invites.ExpireInvites();
        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return Daily(TimeSpan.Zero);
    }
}
