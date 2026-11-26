namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// Whether members may set or change their own password. Administrators can always change a member's password.
/// </summary>
public enum PasswordChangeMode
{
    /// <summary>Members set and change their own password, held to the rules.</summary>
    Allowed,

    /// <summary>Members set a password while theirs is empty, and cannot change one they have.</summary>
    InitialOnly,

    /// <summary>Members can neither set nor change a password. Only administrators can.</summary>
    Disallowed
}
