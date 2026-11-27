using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The Notifications secrets, each encrypted, kept in their own file so the plugin configuration never holds them.
/// </summary>
public sealed class NotificationSecrets
{
    /// <summary>Gets or sets the SMTP password.</summary>
    public string SmtpPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets the VAPID private key browser notifications are signed with.</summary>
    public string PushPrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets each channel's secrets, by channel id.</summary>
    public Dictionary<string, NotificationChannelSecrets> Channels { get; set; } = new(StringComparer.Ordinal);
}
