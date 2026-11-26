namespace Jellyfin.Plugin.Necto.Configuration.Notifications;

/// <summary>
/// The kind of place an administrators' channel sends events to.
/// </summary>
public enum NotificationChannelKind
{
    /// <summary>A Discord channel webhook, which gets an embed.</summary>
    Discord = 0,

    /// <summary>Any address that takes a POST, which gets JSON or a custom body.</summary>
    Webhook = 1,

    /// <summary>Email to the addresses the channel lists, sent through the SMTP server in Settings.</summary>
    Email = 2,

    /// <summary>Every administrator's Jellyfin apps and the browsers they turned notifications on in.</summary>
    Administrators = 3,

    /// <summary>Jellyfin's activity log.</summary>
    ActivityLog = 4
}
