namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may do in Requests. A quota counts requests in the window set in Settings, and 0 is no limit.
/// </summary>
public class RequestPermissions
{
    /// <summary>Gets or sets a value indicating whether members may request media.</summary>
    public bool MayRequest { get; set; }

    /// <summary>Gets or sets a value indicating whether members' requests are approved without review.</summary>
    public bool AutoApprove { get; set; }

    /// <summary>Gets or sets how many movies a member may request in the window. 0 is no limit.</summary>
    public int MovieQuota { get; set; }

    /// <summary>Gets or sets how many series a member may request in the window. 0 is no limit.</summary>
    public int SeriesQuota { get; set; }

    /// <summary>Gets or sets a value indicating whether members see other users' requests.</summary>
    public bool MaySeeOthers { get; set; }

    /// <summary>Gets or sets a value indicating whether members may approve, decline, and remove requests.</summary>
    public bool MayManage { get; set; }
}
