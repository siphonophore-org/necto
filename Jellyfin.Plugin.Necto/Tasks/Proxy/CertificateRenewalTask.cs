using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Proxy;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Tasks.Proxy;

/// <summary>
/// Keeps every route's certificate current: asks Let's Encrypt for one that is missing, no longer covers the route,
/// or is within 30 days of expiry, reloads certificate files, replaces a self-signed one before it expires, and
/// warns when a certificate is about to run out or cannot be loaded. The proxy also notices file changes on its
/// own, so this is the scheduled safety net. Asking for a new certificate at once is the route's button.
/// </summary>
public sealed class CertificateRenewalTask : PluginScheduledTask
{
    private static readonly TimeSpan _warnBefore = TimeSpan.FromDays(14);

    private readonly ProxyManager _proxy;
    private readonly ActivityLogger _activity;
    private readonly INotifier _notifier;
    private readonly ILogger<CertificateRenewalTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateRenewalTask"/> class.
    /// </summary>
    /// <param name="proxy">The proxy manager.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="notifier">Sends expiring and failed certificates to Notifications.</param>
    /// <param name="logger">The logger.</param>
    public CertificateRenewalTask(ProxyManager proxy, ActivityLogger activity, INotifier notifier, ILogger<CertificateRenewalTask> logger)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(notifier);
        _proxy = proxy;
        _activity = activity;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public override string Name => ServerText.Get(TranslationKeys.ProxyTaskRefreshCertificate);

    /// <inheritdoc />
    public override string Key => "NectoProxyCertificates";

    /// <inheritdoc />
    public override string Description => ServerText.Get(TranslationKeys.ProxyTaskRefreshCertificateDescription);

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

        var routes = await _proxy.RefreshCertificatesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var nectoEvent in Review(routes, DateTime.UtcNow))
        {
            await _notifier.SendAsync(nectoEvent, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    /// <summary>
    /// Writes the activity entries for certificates that failed to load or expire soon, and gives their events.
    /// </summary>
    /// <param name="routes">Each route's status after the refresh.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The events to send.</returns>
    internal List<NectoEvent> Review(IEnumerable<RouteStatus> routes, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var events = new List<NectoEvent>();
        foreach (var route in routes)
        {
            var status = route.Certificate;
            if (status.Mode == CertificateMode.None)
            {
                continue;
            }

            // Let's Encrypt failures are reported where they happen, with the reason and the next try.
            if (!status.Loaded && status.Mode != CertificateMode.Automatic)
            {
                _logger.LogError("The certificate for {Host} could not be loaded: {Error}", route.Host, status.Error);
                _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityCertificateFailedToLoad), "Necto.Proxy.CertificateFailed", ActivityEntries.Plain(status.Error), LogLevel.Error);
                events.Add(new NectoEvent(ProxyEvents.CertificateFailed, ServerText.Format(TranslationKeys.ProxyEventCertificateFailedTitle, route.Host), status.Error) { Level = NectoEventLevel.Error });
            }
            else if (status.Loaded && status.NotAfterUtc is { } expires && expires - now < _warnBefore)
            {
                var day = expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var overview = ServerText.Format(TranslationKeys.ProxyActivityCertificateExpiringOverview, string.Join(", ", status.Names), day);
                _logger.LogWarning("The certificate for {Names} expires {Expires:yyyy-MM-dd}, renew it so the route keeps working", string.Join(", ", status.Names), expires);
                _activity.Log(ServerText.Get(TranslationKeys.ProxyActivityCertificateExpiring), "Necto.Proxy.CertificateExpiring", overview, LogLevel.Warning);
                events.Add(new NectoEvent(ProxyEvents.CertificateExpiring, ServerText.Format(TranslationKeys.ProxyEventCertificateExpiringTitle, route.Host), overview) { Level = NectoEventLevel.Warning });
            }
        }

        return events;
    }

    /// <inheritdoc />
    public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Seeds the default schedule only. Administrators change it under Scheduled Tasks.
        yield return Daily(TimeSpan.FromHours(3));
    }
}
