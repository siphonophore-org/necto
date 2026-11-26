using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The events members receive from Notifications.
/// </summary>
public class NotificationPermissions
{
    /// <summary>Gets or sets a value indicating whether members receive every event.</summary>
    public bool AllEvents { get; set; }

    /// <summary>Gets or sets the keys of the events members receive.</summary>
    public List<string> EventKeys { get; set; } = new();
}
