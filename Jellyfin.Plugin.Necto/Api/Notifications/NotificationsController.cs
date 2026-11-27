using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Notifications;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// The routes of Notifications: the events, the channels and their tests, the settings, the send log, and the web push
/// key, subscriptions, and service worker the user pages share.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Notifications")]
[Produces(MediaTypeNames.Application.Json)]
public class NotificationsController : ControllerBase
{
    private readonly Notifier _notifier;
    private readonly StolonNotifier _stolon;
    private readonly NotificationSettingsService _settings;
    private readonly NotificationLog _log;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly NotificationPushStore? _push;
    private readonly NotificationHeldStore? _held;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationsController"/> class.
    /// </summary>
    /// <param name="notifier">Sends tests.</param>
    /// <param name="stolon">Builds previews and keeps subscriptions.</param>
    /// <param name="settings">Reads and saves the settings.</param>
    /// <param name="log">The send log.</param>
    /// <param name="store">Reads the configuration.</param>
    /// <param name="userManager">Names the users with browsers.</param>
    /// <param name="push">The browsers users turned notifications on in.</param>
    /// <param name="held">The held Jellyfin messages.</param>
    public NotificationsController(INotifier notifier, StolonNotifier stolon, NotificationSettingsService settings, NotificationLog log, IConfigurationStore store, IUserManager userManager, IWebPushStore push, INotifyHeldStore held)
    {
        _notifier = notifier as Notifier ?? throw new ArgumentException("Notifications needs its own notifier.", nameof(notifier));
        _stolon = stolon ?? throw new ArgumentNullException(nameof(stolon));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _push = push as NotificationPushStore;
        _held = held as NotificationHeldStore;
    }

    /// <summary>
    /// Lists every event a tool may send, by tool in sidebar order.
    /// </summary>
    /// <returns>Each event's key, name, tool, and whether it can go to the user it is about.</returns>
    [HttpGet("Events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<NotificationEventView>> Events()
    {
        var language = StolonTranslations.RequestLanguage(Request);
        return Ok(NectoEventCatalog.All.Select(e => new NotificationEventView(e.Key, StolonTranslations.Get(e.NameKey, language), e.Tool, e.ForUsers)));
    }

    /// <summary>
    /// Lists the channels the Notifications page shows: browser notifications, Jellyfin messages, then every
    /// administrators' channel.
    /// </summary>
    /// <returns>The channels.</returns>
    [HttpGet("Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<NotificationChannelView>> Channels()
    {
        var settings = _store.Read(c => NotificationNormalizer.Normalize(ConfigurationCopy.Of(c.Notifications ?? new NotificationSettings())));
        return Ok(NotificationChannelView.List(settings));
    }

    /// <summary>
    /// Sends a test to one channel. A channel that goes to people sends it to the administrator who asked.
    /// </summary>
    /// <param name="id">The channel's id.</param>
    /// <param name="cancellationToken">Stops the send.</param>
    /// <returns>Whether it was sent and what happened.</returns>
    [HttpPost("Channels/{id}/Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResult>> Test([FromRoute] string id, CancellationToken cancellationToken)
    {
        var result = await _notifier.TestAsync(id, Caller.UserId(User), StolonTranslations.RequestLanguage(Request), cancellationToken).ConfigureAwait(false);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Shows what a channel as the page has it would get for the test event, without sending it.
    /// </summary>
    /// <param name="channel">The channel as the page has it.</param>
    /// <returns>The preview.</returns>
    [HttpPost("Preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<NotifyPreview> Preview([FromBody] NotificationChannel channel)
    {
        if (channel is null)
        {
            return BadRequest();
        }

        var language = StolonTranslations.RequestLanguage(Request);
        var settings = _store.Read(c => ConfigurationCopy.Of(c.Notifications ?? new NotificationSettings()));
        var test = Notifier.TestEvent(Caller.UserId(User), language);
        var copy = ConfigurationCopy.Of(channel);
        copy.Kind = channel.Kind;
        var message = NotificationMessages.Post(test, copy, settings, StolonTranslations.Get(TranslationKeys.NotificationsEventTest, language), DateTimeOffset.UtcNow, language);
        if (copy.Kind is NotificationChannelKind.Email or NotificationChannelKind.ActivityLog)
        {
            var (subject, body) = NotificationMessages.Email(test, settings);
            return Ok(new NotifyPreview { DestinationId = copy.Id, Channel = NotifyChannel.Jellyfin, Header = subject, Text = body });
        }

        return Ok(StolonNotifier.Preview(copy, message));
    }

    /// <summary>
    /// Gives the settings, with every stored secret hidden.
    /// </summary>
    /// <returns>The settings.</returns>
    [HttpGet("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<NotificationSettings> GetSettings() => Ok(_settings.Read());

    /// <summary>
    /// Saves the settings. A secret sent back as kept stays as stored.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The settings as saved, with their secrets hidden, or why they were refused.</returns>
    [HttpPut("Settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<NotificationSettings> SaveSettings([FromBody] NotificationSettings settings)
    {
        if (settings is null)
        {
            return BadRequest();
        }

        var error = _settings.Save(settings, StolonTranslations.RequestLanguage(Request));
        return error is null ? Ok(_settings.Read()) : BadRequest(new ApiError(error));
    }

    /// <summary>
    /// Gives the send log, newest first.
    /// </summary>
    /// <returns>The entries.</returns>
    [HttpGet("Log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<NotificationLogEntry>> Log() => Ok(_log.List());

    /// <summary>
    /// Clears the send log.
    /// </summary>
    /// <returns>How many entries were cleared.</returns>
    [HttpDelete("Log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CountResult> ClearLog() => Ok(new CountResult(_log.Clear()));

    /// <summary>
    /// Lists the browsers users turned notifications on in, and how many Jellyfin messages wait for an app.
    /// </summary>
    /// <returns>The browsers and the held count.</returns>
    [HttpGet("Push")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PushStatusView> Push()
    {
        var subscriptions = (_push?.All() ?? Array.Empty<WebPushSubscription>())
            .Select(s => new PushBrowserView(_userManager.GetUserById(s.UserId)?.Username ?? string.Empty, s.Label, s.CreatedUtc))
            .ToList();
        return Ok(new PushStatusView(_stolon.GetPublicKey() is { Length: > 0 }, subscriptions, _held?.Counts().Values.Sum() ?? 0));
    }

    /// <summary>
    /// Makes a new VAPID key pair. Every browser must turn notifications on again.
    /// </summary>
    /// <returns>How many browsers were forgotten.</returns>
    [HttpPost("Push/Keys")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CountResult> ResetKeys() => Ok(new CountResult(_push?.ResetKeys() ?? 0));

    /// <summary>
    /// Drops every Jellyfin message waiting for an app to come online.
    /// </summary>
    /// <returns>How many were dropped.</returns>
    [HttpDelete("Held")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<CountResult> ClearHeld() => Ok(new CountResult(_held?.Clear() ?? 0));

    /// <summary>
    /// Gives a user page the key a browser signs up for notifications with.
    /// </summary>
    /// <returns>The VAPID public key, or 404 while browser notifications are off.</returns>
    [HttpGet("Push/Key")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PushKeyView> PushKey()
    {
        var on = _store.Read(c => c.Notifications?.BrowserNotifications ?? true);
        return on && _stolon.GetPublicKey() is { Length: > 0 } key ? Ok(new PushKeyView(key)) : NotFound();
    }

    /// <summary>
    /// Turns on browser notifications for the signed in user in the browser that sent its subscription.
    /// </summary>
    /// <param name="body">The browser's subscription.</param>
    /// <returns>No content, or 400 when the subscription is not valid.</returns>
    [HttpPost("Push/Subscribe")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Subscribe([FromBody] PushSubscriptionBody body)
    {
        var userId = Caller.UserId(User);
        return body is not null && userId != Guid.Empty && _stolon.Subscribe(userId, body.Endpoint, body.Keys?.P256dh, body.Keys?.Auth, body.Label) ? NoContent() : BadRequest();
    }

    /// <summary>
    /// Turns off browser notifications for the signed in user in one of their browsers.
    /// </summary>
    /// <param name="body">The browser's subscription, of which only the endpoint is read.</param>
    /// <returns>No content, or 404 when the user has no such browser.</returns>
    [HttpPost("Push/Unsubscribe")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Unsubscribe([FromBody] PushSubscriptionBody body)
        => body is not null && _stolon.Unsubscribe(Caller.UserId(User), body.Endpoint) ? NoContent() : NotFound();

    /// <summary>
    /// Serves the service worker every user page shares. It may control the whole server's address, so one browser
    /// subscription serves every user page.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("Push/Worker.js")]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ContentResult Worker()
    {
        Response.Headers["Service-Worker-Allowed"] = "/";
        return WebPushWorker.Serve(Response);
    }
}
