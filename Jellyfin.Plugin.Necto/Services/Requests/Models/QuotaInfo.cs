using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A user's request allowance for one type over a rolling window: movies for movies, seasons for shows.
/// </summary>
public class QuotaInfo
{
    /// <summary>Gets or sets how many movies or seasons the window allows.</summary>
    public int Limit { get; set; }

    /// <summary>Gets or sets the window length in days.</summary>
    public int Days { get; set; }

    /// <summary>Gets or sets how many movies or seasons the user requested in the window.</summary>
    public int Used { get; set; }

    /// <summary>Gets or sets when enough leaves the window for the next request, when the quota is used up.</summary>
    public DateTime? NextFreeUtc { get; set; }

    /// <summary>Gets the movies or seasons left in the window.</summary>
    public int Remaining => Math.Max(0, Limit - Used);

    /// <summary>Gets or sets what the request being made takes from it: 1, or 0 for a show already counted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Need { get; set; } = 1;
}
