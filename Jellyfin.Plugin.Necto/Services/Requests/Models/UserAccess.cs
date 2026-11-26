using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// What the signed-in user may do, as reported to the page.
/// </summary>
public class UserAccess
{
    /// <summary>Gets or sets the Jellyfin account, so library lookups show only what this user may see.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Jellyfin.Database.Implementations.Entities.User? User { get; set; }

    /// <summary>Gets or sets everything the user's permissions decide, for the checks a request goes through.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RequestGrant? Permissions { get; set; }

    /// <summary>Gets or sets the language of the messages the caller is shown, from their browser, or null for US English.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Language { get; set; }

    /// <summary>Gets or sets the most seasons of one show they may request; 0 means no limit.</summary>
    public int MaxSeasonsPerShow { get; set; }

    /// <summary>Gets or sets a value indicating whether a show's specials can be requested, each on its own (the Allow specials setting).</summary>
    public bool AllowSpecials { get; set; }

    /// <summary>Gets or sets the user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user manages requests: an administrator, or in a group that may manage them.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>Gets or sets a value indicating whether the user manages issues: an administrator, or in a group that may manage them.</summary>
    public bool ManagesIssues { get; set; }

    /// <summary>Gets or sets a value indicating whether the user may request.</summary>
    public bool CanRequest { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's movie requests are approved straight away.</summary>
    public bool AutoApproveMovies { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's show requests are approved straight away.</summary>
    public bool AutoApproveShows { get; set; }

    /// <summary>Gets or sets a value indicating whether the user sees everyone's requests.</summary>
    public bool SeeAllRequests { get; set; }

    /// <summary>Gets or sets a value indicating whether the user sees who made other people's requests.</summary>
    public bool SeeRequesters { get; set; }

    /// <summary>Gets or sets the movie profiles the user picks from when requesting; empty when Radarr isn't set up.</summary>
    public List<ProfileOption> MovieProfiles { get; set; } = new();

    /// <summary>Gets or sets the show profiles the user picks from when requesting; empty when Sonarr isn't set up.</summary>
    public List<ProfileOption> ShowProfiles { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether movies can be requested.</summary>
    public bool Movies { get; set; }

    /// <summary>Gets or sets a value indicating whether shows can be requested.</summary>
    public bool Series { get; set; }

    /// <summary>Gets or sets a value indicating whether issues are on, so the menu and the issue page show.</summary>
    public bool IssuesEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether the user may report issues: issues are on and their permissions allow it.</summary>
    public bool ReportIssues { get; set; }

    /// <summary>Gets or sets the user's open request limit, 0 for none.</summary>
    public int MaxOpenRequests { get; set; }

    // The counts below are read from the database, and most calls never look at them, so each is worked out the first
    // time it's read (once) rather than for every call.
    private Lazy<int>? _openRequests;
    private Lazy<QuotaInfo?>? _movieQuota;
    private Lazy<QuotaInfo?>? _showQuota;

    /// <summary>Gets or sets how many requests the user has open.</summary>
    public int OpenRequests { get => _openRequests?.Value ?? 0; set => _openRequests = new Lazy<int>(value); }

    /// <summary>Gets or sets the movie quota, or null when movies are not limited for this user.</summary>
    public QuotaInfo? MovieQuota { get => _movieQuota?.Value; set => _movieQuota = new Lazy<QuotaInfo?>(value); }

    /// <summary>Gets or sets the show quota, or null when shows are not limited for this user.</summary>
    public QuotaInfo? ShowQuota { get => _showQuota?.Value; set => _showQuota = new Lazy<QuotaInfo?>(value); }

    /// <summary>Has the open requests and quotas counted when first read, rather than now.</summary>
    /// <param name="openRequests">Counts the open requests.</param>
    /// <param name="movieQuota">Works out the movie quota.</param>
    /// <param name="showQuota">Works out the show quota.</param>
    /// <returns>This access.</returns>
    internal UserAccess CountedWhenRead(Func<int> openRequests, Func<QuotaInfo?> movieQuota, Func<QuotaInfo?> showQuota)
    {
        _openRequests = new Lazy<int>(openRequests);
        _movieQuota = new Lazy<QuotaInfo?>(movieQuota);
        _showQuota = new Lazy<QuotaInfo?>(showQuota);
        return this;
    }
}
