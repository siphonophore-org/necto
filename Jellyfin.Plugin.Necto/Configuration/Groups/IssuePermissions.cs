namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may do in Issues.
/// </summary>
public class IssuePermissions
{
    /// <summary>Gets or sets a value indicating whether members may report an issue.</summary>
    public bool MayReport { get; set; }

    /// <summary>Gets or sets a value indicating whether members may change and close issues.</summary>
    public bool MayManage { get; set; }
}
