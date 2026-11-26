using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// How many requests are in each state: everyone's, with each requester's, or one user's, with what they have left.
/// </summary>
public class RequestStats
{
    /// <summary>Gets or sets the user these are for; null for everyone's.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Gets or sets the counts by status name.</summary>
    public Dictionary<string, int> ByStatus { get; set; } = new();

    /// <summary>Gets or sets the total.</summary>
    public int Total { get; set; }

    /// <summary>Gets or sets how many are for movies.</summary>
    public int Movies { get; set; }

    /// <summary>Gets or sets how many are for shows.</summary>
    public int Series { get; set; }

    /// <summary>Gets or sets one user's movie quota, or null without one.</summary>
    public QuotaInfo? MovieQuota { get; set; }

    /// <summary>Gets or sets one user's show quota, or null without one.</summary>
    public QuotaInfo? ShowQuota { get; set; }

    /// <summary>Gets or sets one user's requests open now, which count toward <see cref="MaxOpenRequests"/>.</summary>
    public int? OpenRequests { get; set; }

    /// <summary>Gets or sets the most requests one user may have open; 0 for no limit.</summary>
    public int? MaxOpenRequests { get; set; }

    /// <summary>Gets or sets each requester's counts, in everyone's; null for one user's.</summary>
    public List<RequestStats>? Users { get; set; }

    /// <summary>Gets or sets the requester's name, in each requester's counts.</summary>
    public string? UserName { get; set; }
}
