using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.Notifications;

/// <summary>
/// One administrators' channel and the events it gets. Stolon's <see cref="NotifyDestination"/> holds the address,
/// headers, custom body, and Discord name, and this adds the channel's kind, its events, and its email addresses.
/// </summary>
public class NotificationChannel : NotifyDestination
{
    /// <summary>The id of the built in Administrators channel.</summary>
    public const string AdministratorsId = "administrators";

    /// <summary>The id of the built in Activity log channel.</summary>
    public const string ActivityLogId = "activity-log";

    private NotificationChannelKind _kind;

    /// <summary>Gets or sets the channel's kind. Setting it sets the Stolon channel it is sent through.</summary>
    public NotificationChannelKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Channel = ChannelFor(value);
        }
    }

    /// <summary>Gets or sets a value indicating whether every event goes here.</summary>
    public bool AllEvents { get; set; }

    /// <summary>Gets or sets the keys of the events that go here.</summary>
    public List<string> EventKeys { get; set; } = new();

    /// <summary>Gets or sets the addresses an email channel sends to, one a line or split by commas.</summary>
    public string Recipients { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the channel is one Necto keeps, which cannot be removed.</summary>
    public bool IsBuiltIn => Kind is NotificationChannelKind.Administrators or NotificationChannelKind.ActivityLog;

    /// <summary>
    /// Gets the Stolon channel a kind is sent through. Email and the activity log are sent by Necto, so they keep no address.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The Stolon channel.</returns>
    public static NotifyChannel ChannelFor(NotificationChannelKind kind) => kind switch
    {
        NotificationChannelKind.Discord => NotifyChannel.Discord,
        NotificationChannelKind.Webhook => NotifyChannel.Webhook,
        _ => NotifyChannel.Jellyfin
    };

    /// <summary>
    /// Works out whether an event goes here.
    /// </summary>
    /// <param name="eventKey">The event.</param>
    /// <returns><c>true</c> when the channel is on and takes every event or names this one.</returns>
    public bool Takes(string eventKey) => Enabled && (AllEvents || EventKeys.Contains(eventKey));
}
