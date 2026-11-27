using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Sends every event to the people and channels it goes to: the user it is about in their browsers and on their
/// Jellyfin apps when their groups give them the event, and each administrators' channel that picks it. Each send is
/// written to the send log, and a channel that fails never stops the rest.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(INotifier))]
public sealed class Notifier : INotifier
{
    private readonly StolonNotifier _stolon;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly IEmailSender _email;
    private readonly ActivityLogger _activity;
    private readonly SecretProtector _secrets;
    private readonly NotificationSecretStore _kept;
    private readonly NotificationLog _log;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly IWebPushStore? _push;
    private readonly ILogger<Notifier> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="Notifier"/> class.
    /// </summary>
    /// <param name="stolon">Sends to Discord, webhooks, Jellyfin apps, and browsers.</param>
    /// <param name="store">Reads the channels and the groups.</param>
    /// <param name="userManager">Lists the users, for the administrators and the user an event is about.</param>
    /// <param name="email">Sends email.</param>
    /// <param name="activity">Writes the activity log channel.</param>
    /// <param name="secrets">Reads the SMTP password.</param>
    /// <param name="kept">Holds the channels' addresses and the SMTP password.</param>
    /// <param name="log">Keeps each send for Troubleshooting.</param>
    /// <param name="serverConfig">Gives the server's language for the event names.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="push">Says who turned browser notifications on, or <c>null</c> when web push is not set up.</param>
    /// <param name="time">The clock, or <c>null</c> for the system's.</param>
    public Notifier(
        StolonNotifier stolon,
        IConfigurationStore store,
        IUserManager userManager,
        IEmailSender email,
        ActivityLogger activity,
        SecretProtector secrets,
        NotificationSecretStore kept,
        NotificationLog log,
        IServerConfigurationManager serverConfig,
        ILogger<Notifier> logger,
        IWebPushStore? push = null,
        TimeProvider? time = null)
    {
        _stolon = stolon ?? throw new ArgumentNullException(nameof(stolon));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _kept = kept ?? throw new ArgumentNullException(nameof(kept));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _push = push;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        try
        {
            var config = _store.Read(ConfigurationCopy.Of);
            config.Notifications = _kept.Fill(NotificationNormalizer.Normalize(config.Notifications));
            if (NectoEventCatalog.Find(nectoEvent.Key) is null)
            {
                _logger.LogDebug("Event {Key} is in no event list, so only channels that take every event get it", FormatUtilities.SanitizeForLog(nectoEvent.Key));
            }

            var sends = NotificationRouter.Plan(config, nectoEvent, Users());
            await Task.WhenAll(sends.Select(s => DeliverAsync(config, nectoEvent, s, false, null, cancellationToken))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send event {Key}", FormatUtilities.SanitizeForLog(nectoEvent.Key));
        }
    }

    /// <summary>
    /// Sends a test to one channel. The browser and Jellyfin app channels, and Administrators, send it to the
    /// administrator who asked.
    /// </summary>
    /// <param name="channelId">The channel's id, or <see cref="NotificationRouter.BrowsersId"/> or <see cref="NotificationRouter.AppsId"/>.</param>
    /// <param name="userId">The administrator asking.</param>
    /// <param name="language">The language of the answer, or <c>null</c> for US English.</param>
    /// <param name="cancellationToken">Stops the send.</param>
    /// <returns>What happened, or <c>null</c> when no channel has the id.</returns>
    public async Task<NotificationResult?> TestAsync(string channelId, Guid userId, string? language, CancellationToken cancellationToken)
    {
        var config = _store.Read(ConfigurationCopy.Of);
        config.Notifications = _kept.Fill(NotificationNormalizer.Normalize(config.Notifications));
        var test = TestEvent(userId, language);
        NotificationSend? send = channelId switch
        {
            NotificationRouter.BrowsersId => new NotificationSend(NotificationTarget.UserBrowsers, channelId, new[] { userId }),
            NotificationRouter.AppsId => new NotificationSend(NotificationTarget.UserApps, channelId, new[] { userId }),
            _ => config.Notifications.Channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.Ordinal)) is { } channel
                ? new NotificationSend(TargetOf(channel.Kind), channel.Id, channel.Kind == NotificationChannelKind.Administrators ? new[] { userId } : Array.Empty<Guid>())
                : null
        };

        if (send is null)
        {
            return null;
        }

        return await DeliverAsync(config, test, send, true, language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the test event, which goes to the administrator who asked for it.
    /// </summary>
    /// <param name="userId">The administrator.</param>
    /// <param name="language">The language it is written in, or <c>null</c> for US English.</param>
    /// <returns>The event.</returns>
    public static NectoEvent TestEvent(Guid userId, string? language) => new(
        NotificationsEvents.Test,
        StolonTranslations.Get(TranslationKeys.NotificationsTestTitle, language),
        StolonTranslations.Get(TranslationKeys.NotificationsTestBody, language))
    {
        UserId = userId,
        Url = "web/"
    };

    /// <summary>
    /// Gets the send a channel kind makes.
    /// </summary>
    /// <param name="kind">The channel's kind.</param>
    /// <returns>The target.</returns>
    public static NotificationTarget TargetOf(NotificationChannelKind kind) => kind switch
    {
        NotificationChannelKind.Discord => NotificationTarget.Discord,
        NotificationChannelKind.Webhook => NotificationTarget.Webhook,
        NotificationChannelKind.Email => NotificationTarget.Email,
        NotificationChannelKind.Administrators => NotificationTarget.Administrators,
        _ => NotificationTarget.ActivityLog
    };

    /// <summary>
    /// Lists every user with what routing needs to know.
    /// </summary>
    private List<NotificationUser> Users()
        => _userManager.GetUsers()
            .Select(u => new NotificationUser(u.Id, u.HasPermission(PermissionKind.IsAdministrator), u.HasPermission(PermissionKind.IsDisabled)))
            .ToList();

    /// <summary>
    /// Sends one planned send and writes it to the send log.
    /// </summary>
    private async Task<NotificationResult> DeliverAsync(PluginConfiguration config, NectoEvent nectoEvent, NotificationSend send, bool test, string? language, CancellationToken cancellationToken)
    {
        var settings = config.Notifications;
        var channel = settings.Channels.FirstOrDefault(c => string.Equals(c.Id, send.ChannelId, StringComparison.Ordinal));
        NotificationResult result;
        try
        {
            result = await DeliverCoreAsync(settings, channel, nectoEvent, send, language, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send event {Key} to {Channel}", FormatUtilities.SanitizeForLog(nectoEvent.Key), send.ChannelId);
            result = new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultNotSent, language));
        }

        _log.Add(new NotificationLogEntry(_time.GetUtcNow().UtcDateTime, nectoEvent.Key, nectoEvent.Title ?? string.Empty, ChannelName(send, channel, language), result.Ok, result.Message, test));
        return result;
    }

    private async Task<NotificationResult> DeliverCoreAsync(NotificationSettings settings, NotificationChannel? channel, NectoEvent nectoEvent, NotificationSend send, string? language, CancellationToken cancellationToken)
    {
        var serverLanguage = StolonTranslations.ServerLanguage(_serverConfig);
        var info = NectoEventCatalog.Find(nectoEvent.Key);
        var name = info is null ? nectoEvent.Key : StolonTranslations.Get(info.NameKey, serverLanguage);
        var now = _time.GetUtcNow();

        switch (send.Target)
        {
            case NotificationTarget.UserBrowsers:
                return await PushAsync(settings, nectoEvent, name, now, send.Recipients, language, cancellationToken).ConfigureAwait(false);
            case NotificationTarget.UserApps:
                return await AppsAsync(settings, nectoEvent, name, now, send.Recipients, cancellationToken).ConfigureAwait(false);
            case NotificationTarget.Administrators:
                var apps = await AppsAsync(settings, nectoEvent, name, now, send.Recipients, cancellationToken).ConfigureAwait(false);
                var browsers = send.Recipients.Where(HasBrowsers).ToList();
                if (browsers.Count == 0)
                {
                    return apps;
                }

                var pushed = await PushAsync(settings, nectoEvent, name, now, browsers, language, cancellationToken).ConfigureAwait(false);
                return new NotificationResult(apps.Ok || pushed.Ok, apps.Message + " " + pushed.Message);
            case NotificationTarget.Discord:
            case NotificationTarget.Webhook:
                if (channel is null)
                {
                    return new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultNotSent, language));
                }

                var posted = await _stolon.SendAsync(channel, NotificationMessages.Post(nectoEvent, channel, settings, name, now, serverLanguage), cancellationToken).ConfigureAwait(false);
                return new NotificationResult(posted.Ok, posted.Message);
            case NotificationTarget.Email:
                return await EmailAsync(settings, channel, nectoEvent, language, cancellationToken).ConfigureAwait(false);
            default:
                _activity.Log(
                    ActivityText.Sentence(nectoEvent.Title) ?? nectoEvent.Key,
                    "Necto." + nectoEvent.Key,
                    ActivityText.Sentence(nectoEvent.Body),
                    nectoEvent.Level switch
                    {
                        NectoEventLevel.Error => LogLevel.Error,
                        NectoEventLevel.Warning => LogLevel.Warning,
                        _ => LogLevel.Information
                    },
                    nectoEvent.UserId ?? Guid.Empty);
                return new NotificationResult(true, StolonTranslations.Get(TranslationKeys.NotificationsResultLogged, language));
        }
    }

    private async Task<NotificationResult> PushAsync(NotificationSettings settings, NectoEvent nectoEvent, string name, DateTimeOffset now, IReadOnlyList<Guid> recipients, string? language, CancellationToken cancellationToken)
    {
        var message = NotificationMessages.Build(nectoEvent, settings, name, now, StolonTranslations.ServerLanguage(_serverConfig));

        // A browser opens a relative address against the worker's scope, which is the server's root.
        message.LinkUrl = nectoEvent.Url ?? message.LinkUrl;
        foreach (var id in recipients)
        {
            message.Recipients.Add(id);
        }

        if (!recipients.Any(HasBrowsers))
        {
            return new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultNoBrowsers, language));
        }

        var result = await _stolon.SendAsync(new NotifyDestination { Id = NotificationRouter.BrowsersId, Channel = NotifyChannel.WebPush }, message, cancellationToken).ConfigureAwait(false);
        return new NotificationResult(result.Ok, result.Message);
    }

    private async Task<NotificationResult> AppsAsync(NotificationSettings settings, NectoEvent nectoEvent, string name, DateTimeOffset now, IReadOnlyList<Guid> recipients, CancellationToken cancellationToken)
    {
        var message = NotificationMessages.Build(nectoEvent, settings, name, now, StolonTranslations.ServerLanguage(_serverConfig));
        message.Subject = string.IsNullOrWhiteSpace(nectoEvent.Subject) ? nectoEvent.Key : nectoEvent.Subject;
        foreach (var id in recipients)
        {
            message.Recipients.Add(id);
        }

        var result = await _stolon.SendAsync(new NotifyDestination { Id = NotificationRouter.AppsId, Channel = NotifyChannel.Jellyfin }, message, cancellationToken).ConfigureAwait(false);
        return new NotificationResult(result.Ok, result.Message);
    }

    private async Task<NotificationResult> EmailAsync(NotificationSettings settings, NotificationChannel? channel, NectoEvent nectoEvent, string? language, CancellationToken cancellationToken)
    {
        if (!settings.Smtp.IsSet)
        {
            return new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultNoSmtp, language));
        }

        var to = NotificationNormalizer.Addresses(channel?.Recipients);
        if (to.Count == 0)
        {
            return new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultNoAddresses, language));
        }

        var (subject, body) = NotificationMessages.Email(NotificationMessages.For(nectoEvent, channel), settings);
        try
        {
            await _email.SendAsync(settings.Smtp, _secrets.Unprotect(settings.Smtp.Password), to, subject, body, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or FormatException or InvalidOperationException or System.Net.Sockets.SocketException or System.IO.IOException or System.Security.Authentication.AuthenticationException)
        {
            _logger.LogWarning(ex, "Could not send email for event {Key}", FormatUtilities.SanitizeForLog(nectoEvent.Key));
            return new NotificationResult(false, StolonTranslations.Get(TranslationKeys.NotificationsResultEmailFailed, language));
        }

        return new NotificationResult(true, StolonTranslations.Get(TranslationKeys.NotificationsResultEmailSent, language));
    }

    private bool HasBrowsers(Guid userId) => _push is not null && userId != Guid.Empty && _push.GetSubscriptions(userId).Count > 0;

    private static string ChannelName(NotificationSend send, NotificationChannel? channel, string? language) => send.Target switch
    {
        NotificationTarget.UserBrowsers => StolonTranslations.Get(TranslationKeys.LabelBrowserNotifications, language),
        NotificationTarget.UserApps => StolonTranslations.Get(TranslationKeys.NotificationsChannelApps, language),
        NotificationTarget.Administrators => StolonTranslations.Get(TranslationKeys.LabelAdministrators, language),
        NotificationTarget.ActivityLog => StolonTranslations.Get(TranslationKeys.NotificationsChannelActivityLog, language),
        _ => channel?.Name is { Length: > 0 } named ? named : send.ChannelId
    };
}
