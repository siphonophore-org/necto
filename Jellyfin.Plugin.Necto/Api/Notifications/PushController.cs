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
/// The browser notification routes a user page calls for its signed in user, who need not be an administrator.
/// </summary>
[ApiController]
[Authorize]
[Route("Notifications/Push")]
[Produces(MediaTypeNames.Application.Json)]
public class PushController : ControllerBase
{
    private readonly StolonNotifier _stolon;
    private readonly IConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="PushController"/> class.
    /// </summary>
    /// <param name="stolon">Gives the key and keeps subscriptions.</param>
    /// <param name="store">Reads whether browser notifications are on.</param>
    public PushController(StolonNotifier stolon, IConfigurationStore store)
    {
        _stolon = stolon ?? throw new ArgumentNullException(nameof(stolon));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Gives a user page the key a browser signs up for notifications with.
    /// </summary>
    /// <returns>The VAPID public key, or 404 while browser notifications are off.</returns>
    [HttpGet("Key")]
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
    [HttpPost("Subscribe")]
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
    [HttpPost("Unsubscribe")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Unsubscribe([FromBody] PushSubscriptionBody body)
        => body is not null && _stolon.Unsubscribe(Caller.UserId(User), body.Endpoint) ? NoContent() : NotFound();
}
