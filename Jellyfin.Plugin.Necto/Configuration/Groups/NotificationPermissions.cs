using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// How members get the events about them, by delivery method. Each list holds the keys of the events members get that
/// way, and an event in neither list is off for members.
/// </summary>
public class NotificationPermissions
{
    /// <summary>
    /// Gets or sets the events members get in the browsers they turned notifications on in. Null marks a stored group
    /// whose events are still in <see cref="EventKeys"/>, which normalizing moves here.
    /// </summary>
    public List<string>? BrowserEventKeys { get; set; }

    /// <summary>
    /// Gets or sets the events members get as messages on their Jellyfin apps. Null marks a stored group, as
    /// <see cref="BrowserEventKeys"/> does.
    /// </summary>
    public List<string>? MessageEventKeys { get; set; }

    /// <summary>Gets or sets a value indicating whether a stored group gave every event, read only to move it.</summary>
    public bool AllEvents { get; set; }

    /// <summary>Gets or sets the events a stored group gave, read only to move them.</summary>
    public List<string> EventKeys { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether a stored group sent its events to browsers, read only to move it.</summary>
    public bool BrowserNotifications { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a stored group sent its events as messages, read only to move it.</summary>
    public bool JellyfinMessages { get; set; } = true;
}
