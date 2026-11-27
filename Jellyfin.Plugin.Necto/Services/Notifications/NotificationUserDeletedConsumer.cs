using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Forgets a deleted user's browsers and the Jellyfin messages held for them.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IEventConsumer<UserDeletedEventArgs>))]
public sealed class NotificationUserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly JsonWebPushStore? _push;
    private readonly JsonNotifyHeldStore? _held;
    private readonly ILogger<NotificationUserDeletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationUserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="push">The browsers users turned notifications on in.</param>
    /// <param name="held">The held Jellyfin messages.</param>
    /// <param name="logger">Writes the server log.</param>
    public NotificationUserDeletedConsumer(IWebPushStore push, INotifyHeldStore held, ILogger<NotificationUserDeletedConsumer> logger)
    {
        _push = push as JsonWebPushStore;
        _held = held as JsonNotifyHeldStore;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var userId = eventArgs.Argument.Id;
        try
        {
            _push?.RemoveUser(userId);
            _held?.RemoveUser(userId);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not forget the notifications of deleted user {UserId}", userId);
        }

        return Task.CompletedTask;
    }
}
