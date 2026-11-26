namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The account rules a group applies to its members: password rules, expiry, inactivity, and session cleanup.
/// Administrators are never held to them.
/// </summary>
public class AccountRules
{
    /// <summary>Gets or sets the password rules.</summary>
    public PasswordRules Password { get; set; } = new();

    /// <summary>Gets or sets the expiry date and what it does.</summary>
    public ExpiryRule Expiry { get; set; } = new();

    /// <summary>Gets or sets the inactivity rule.</summary>
    public InactivityRule Inactivity { get; set; } = new();

    /// <summary>Gets or sets the session cleanup rules.</summary>
    public SessionRules Sessions { get; set; } = new();
}
