namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One channel's secrets, each encrypted: its address, its headers, and its email addresses.
/// </summary>
public sealed class NotificationChannelSecrets
{
    /// <summary>Gets or sets the Discord or webhook address.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets a webhook's extra headers.</summary>
    public string Headers { get; set; } = string.Empty;

    /// <summary>Gets or sets the addresses an email channel sends to.</summary>
    public string Recipients { get; set; } = string.Empty;
}
