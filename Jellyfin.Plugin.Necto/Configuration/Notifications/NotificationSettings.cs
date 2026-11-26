using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Notifications;

/// <summary>
/// The Notifications settings, in the Notifications section of Settings.
/// </summary>
public class NotificationSettings
{
    /// <summary>Gets or sets a value indicating whether users get browser notifications for the events their groups give them.</summary>
    public bool BrowserNotifications { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether users get a message on their Jellyfin apps for the events their groups give them.</summary>
    public bool JellyfinMessages { get; set; } = true;

    /// <summary>Gets or sets the server's public address, which links in notifications start with. Blank leaves links relative.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the contact a push service is given, a mailto: or https address. Blank uses the server's address.</summary>
    public string PushContact { get; set; } = string.Empty;

    /// <summary>Gets or sets the VAPID public key browser notifications are signed with.</summary>
    public string PushPublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the VAPID private key, encrypted at rest.</summary>
    public string PushPrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the SMTP server email channels send through.</summary>
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>Gets or sets the administrators' channels, the built in Administrators and Activity log first.</summary>
    public List<NotificationChannel> Channels { get; set; } = new();
}
