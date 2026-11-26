namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Why a change to a group was refused.
/// </summary>
public enum GroupChangeError
{
    /// <summary>The change was saved.</summary>
    None,

    /// <summary>No group has the id.</summary>
    NotFound,

    /// <summary>The group has no name.</summary>
    NameRequired,

    /// <summary>Another group already has the name.</summary>
    NameTaken,

    /// <summary>The change would delete Everyone, which always exists.</summary>
    EveryoneFixed
}
