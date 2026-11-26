namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// Who may open a custom page or see an image, and the tiers a store's read and write access take.
/// </summary>
public enum PageVisibility
{
    /// <summary>Anyone, including visitors who are not signed in.</summary>
    Anonymous,

    /// <summary>Signed in users. A page at this tier opens only for administrators and the groups that give it.</summary>
    User,

    /// <summary>Administrators only.</summary>
    Admin
}
