namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// What the signup page learns about an invite.
/// </summary>
/// <param name="Valid">Whether the invite can make an account now.</param>
/// <param name="Message">The welcome message, or empty.</param>
/// <param name="RequiresPin">Whether the invite asks for a PIN.</param>
/// <param name="Throttled">Whether the caller's address made too many calls.</param>
/// <param name="Error">Why the caller was turned away, or empty.</param>
public sealed record InviteInfoView(bool Valid, string Message, bool RequiresPin, bool Throttled, string Error);
