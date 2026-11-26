namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// The keys a browser's subscription carries, which each notification is encrypted with.
/// </summary>
public class PushSubscriptionKeys
{
    /// <summary>Gets or sets the browser's P-256 public key, base64url.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Gets or sets the browser's authentication secret, base64url.</summary>
    public string Auth { get; set; } = string.Empty;
}
