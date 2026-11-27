namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// Where an invite stands, as the Invites tool shows it.
/// </summary>
public enum InviteState
{
    /// <summary>The invite works.</summary>
    Active,

    /// <summary>The invite locked itself after too many wrong PINs.</summary>
    Locked,

    /// <summary>The invite's day has passed, or the group a new account would join has expired.</summary>
    Expired,

    /// <summary>The invite made as many accounts as it may.</summary>
    UsedUp,

    /// <summary>An administrator disabled the invite.</summary>
    Disabled
}
