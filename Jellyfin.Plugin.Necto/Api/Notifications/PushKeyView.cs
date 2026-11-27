namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// The key a user page signs a browser up for notifications with.
/// </summary>
/// <param name="PublicKey">The server's VAPID public key, base64url.</param>
public sealed record PushKeyView(string PublicKey);
