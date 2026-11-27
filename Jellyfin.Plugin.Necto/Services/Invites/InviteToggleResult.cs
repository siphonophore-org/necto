namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// What turning an invite on or off did.
/// </summary>
public enum InviteToggleResult
{
    /// <summary>The invite was changed.</summary>
    Updated,

    /// <summary>No invite has the id.</summary>
    NotFound,

    /// <summary>The invite stays off, since its group disallows password changes.</summary>
    GroupBlocksInvites,

    /// <summary>The invite stays off, since its day has passed. Move the day instead.</summary>
    Expired
}
