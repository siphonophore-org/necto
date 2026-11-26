using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may do in Requests. A quota counts requests in the window set in Settings, and 0 is no limit.
/// </summary>
public class RequestPermissions
{
    /// <summary>Gets or sets a value indicating whether members may request media.</summary>
    public bool MayRequest { get; set; }

    /// <summary>Gets or sets a value indicating whether members may request movies, while they may request.</summary>
    public bool Movies { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether members may request series, while they may request.</summary>
    public bool Series { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether members may request titles that are not out yet.</summary>
    public bool Unreleased { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether members' movie requests are approved without review.</summary>
    public bool AutoApproveMovies { get; set; }

    /// <summary>Gets or sets a value indicating whether members' series requests are approved without review.</summary>
    public bool AutoApproveSeries { get; set; }

    /// <summary>Gets or sets how many requests a member may have open at once. 0 is no limit.</summary>
    public int MaxOpenRequests { get; set; } = 10;

    /// <summary>Gets or sets how many movies a member may request in the window. 0 is no limit.</summary>
    public int MovieQuota { get; set; }

    /// <summary>Gets or sets how many series a member may request in the window. 0 is no limit.</summary>
    public int SeriesQuota { get; set; }

    /// <summary>Gets or sets how many seasons of one series a member may request. 0 is no limit.</summary>
    public int MaxSeasons { get; set; }

    /// <summary>Gets or sets the movie profiles members pick from. Empty is every profile.</summary>
    public List<string> MovieProfiles { get; set; } = new();

    /// <summary>Gets or sets the series profiles members pick from. Empty is every profile.</summary>
    public List<string> SeriesProfiles { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether members see other users' requests.</summary>
    public bool MaySeeOthers { get; set; }

    /// <summary>Gets or sets a value indicating whether members see who made other users' requests.</summary>
    public bool MaySeeRequesters { get; set; }

    /// <summary>Gets or sets a value indicating whether members may approve, decline, and remove requests.</summary>
    public bool MayManage { get; set; }
}
