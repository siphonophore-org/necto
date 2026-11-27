using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Tasks.Proxy;

/// <summary>
/// Runs the same outside check as Troubleshooting's Test from outside on a schedule, so a router or provider change that
/// closes port 443 shows up in the activity log before people report it. The error is written once when the
/// failure starts and an entry when it clears, not on every run. A check that could not run at all, such as when
/// the outside service is busy, says nothing about the port and changes neither.
/// </summary>
public sealed class ExternalAccessTask : PluginScheduledTask
{
    private readonly ProxyManager _proxy;
    private readonly ActivityLogger _activity;
    private readonly ILogger<ExternalAccessTask> _logger;
    private readonly INotifier _notifier;
    private bool _failing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalAccessTask"/> class.
    /// </summary>
    /// <param name="proxy">The proxy manager.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="notifier">Sends a lost outside check to Notifications.</param>
    public ExternalAccessTask(ProxyManager proxy, ActivityLogger activity, ILogger<ExternalAccessTask> logger, INotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;
        _proxy = proxy;
        _activity = activity;
        _logger = logger;
    }

    /// <inheritdoc />
    public override string Name => ServerText.Get(TranslationKeys.ProxyTaskExternal);

    /// <inheritdoc />
    public override string Key => "NectoProxyExternalAccess";

    /// <inheritdoc />
    public override string Description => ServerText.Get(TranslationKeys.ProxyTaskVerifyExternalAccessDescription);

    /// <inheritdoc />
    public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(0);

        var enabled = Plugin.Instance?.ReadConfiguration(c => c.Proxy?.Enabled == true) ?? false;
        if (!enabled)
        {
            progress.Report(100);
            return;
        }

        var result = await _proxy.CheckReachabilityAsync(cancellationToken).ConfigureAwait(false);
        if (result.Reachable is null)
        {
            _logger.LogDebug("External access could not be verified: {Error}", result.Error);
        }
        else if (result.Reachable == false)
        {
            var tried = string.Join("; ", result.Nodes.Select(n => (n.Location.Length > 0 ? n.Location : ServerText.Get(TranslationKeys.Unknown)) + ": " + n.Error));
            var message = tried.Length > 0 ? result.Error + " " + tried : result.Error;

            // One error when access is lost. Each later run that still fails is detail for diagnosis.
            if (_failing)
            {
                _logger.LogDebug("External access still fails: {Message}", message);
            }
            else
            {
                _failing = true;
                _logger.LogError("External access failed: {Message}", message);
                _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityExternalAccessFailed), "Necto.Proxy.ExternalAccessFailed", ActivityEntries.Plain(message), LogLevel.Error);
                await _notifier.SendAsync(new NectoEvent(ProxyEvents.Unreachable, ServerText.Format(TranslationKeys.ProxyEventUnreachableTitle, Plugin.Instance?.ReadConfiguration(c => ProxyManager.PublicHostname(c.Proxy)) ?? string.Empty), message) { Level = NectoEventLevel.Error }, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (_failing)
        {
            _failing = false;
            _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityExternalAccessRestored), "Necto.Proxy.ExternalAccessRestored", ServerText.Format(TranslationKeys.ProxyActivityExternalAccessRestoredOverview, ReachabilityChecker.Port));
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Seeds the default schedule only. Administrators change it under Scheduled Tasks.
        yield return EveryInterval(TimeSpan.FromHours(1));
    }
}
