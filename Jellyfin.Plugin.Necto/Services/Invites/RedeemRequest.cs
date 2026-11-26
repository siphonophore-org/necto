namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// What the signup page sends to use an invite.
/// </summary>
public class RedeemRequest
{
    /// <summary>Gets or sets the PIN, or empty.</summary>
    public string Pin { get; set; } = string.Empty;

    /// <summary>Gets or sets the user name to make.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the new account's password.</summary>
    public string Password { get; set; } = string.Empty;
}
