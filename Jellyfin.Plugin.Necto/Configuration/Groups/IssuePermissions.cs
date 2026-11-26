namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may do in Issues. A limit counts reports in the window set in Settings, and 0 is no limit.
/// </summary>
public class IssuePermissions
{
    /// <summary>Gets or sets a value indicating whether members may report an issue.</summary>
    public bool MayReport { get; set; }

    /// <summary>Gets or sets how many issues a member may report or join in the window. 0 is no limit.</summary>
    public int Limit { get; set; } = 10;

    /// <summary>Gets or sets a value indicating whether members may change and close issues.</summary>
    public bool MayManage { get; set; }
}
