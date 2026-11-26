namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The password rules a group holds its members to. Members of a group that enforces them move onto Necto's
/// authentication provider, which checks each new password they choose.
/// </summary>
public class PasswordRules
{
    /// <summary>
    /// Gets or sets a value indicating whether the rules are enforced on the group's members.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an empty password is refused. While <c>false</c>, an empty password always
    /// passes, since it means the account has no password.
    /// </summary>
    public bool DisallowEmpty { get; set; } = true;

    /// <summary>Gets or sets the shortest password allowed.</summary>
    public int MinLength { get; set; } = 8;

    /// <summary>Gets or sets a value indicating whether a password needs a capital letter.</summary>
    public bool RequireUppercase { get; set; }

    /// <summary>Gets or sets a value indicating whether a password needs a number.</summary>
    public bool RequireNumber { get; set; }

    /// <summary>Gets or sets a value indicating whether a password needs a symbol.</summary>
    public bool RequireSymbol { get; set; }

    /// <summary>Gets or sets whether members may set or change their own password.</summary>
    public PasswordChangeMode ChangeMode { get; set; } = PasswordChangeMode.Allowed;
}
