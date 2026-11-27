namespace Jellyfin.Plugin.Necto.Configuration.Policies;

/// <summary>
/// When a group writes its Jellyfin preferences onto its members.
/// </summary>
public enum PreferencesMode
{
    /// <summary>On every save and sync, so members always match the group.</summary>
    Maintain = 0,

    /// <summary>Once, when a user joins the group, after which members may change them.</summary>
    OnJoin = 1
}
