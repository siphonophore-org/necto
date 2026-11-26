namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Where one send of an event goes.
/// </summary>
public enum NotificationTarget
{
    /// <summary>The browsers the event's user turned notifications on in.</summary>
    UserBrowsers,

    /// <summary>The event's user's Jellyfin apps, held until one comes online.</summary>
    UserApps,

    /// <summary>Every administrator's Jellyfin apps and browsers.</summary>
    Administrators,

    /// <summary>A Discord channel.</summary>
    Discord,

    /// <summary>A webhook.</summary>
    Webhook,

    /// <summary>Email to a channel's addresses.</summary>
    Email,

    /// <summary>Jellyfin's activity log.</summary>
    ActivityLog
}
