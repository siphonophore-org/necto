using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The events members receive from Notifications, and the channels they receive them on.
/// </summary>
public class NotificationPermissions
{
    /// <summary>Gets or sets a value indicating whether members receive every event.</summary>
    public bool AllEvents { get; set; }

    /// <summary>Gets or sets the keys of the events members receive.</summary>
    public List<string> EventKeys { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether members receive their events in the browsers they turned notifications on in.</summary>
    public bool BrowserNotifications { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether members receive their events as messages on their Jellyfin apps.</summary>
    public bool JellyfinMessages { get; set; } = true;

    /// <summary>
    /// Works out whether members receive any event.
    /// </summary>
    /// <returns><c>true</c> when the group gives every event or names one.</returns>
    public bool ReceivesAny() => AllEvents || (EventKeys?.Count ?? 0) > 0;
}
