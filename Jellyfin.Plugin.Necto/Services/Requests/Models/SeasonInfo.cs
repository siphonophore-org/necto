using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One season of a show, as offered on the request form.
/// </summary>
public class SeasonInfo
{
    /// <summary>Gets or sets the season number.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets how many episodes the season has, when known.</summary>
    public int? EpisodeCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the library has episodes of it.</summary>
    public bool InLibrary { get; set; }

    /// <summary>Gets or sets a value indicating whether an open request already covers it.</summary>
    public bool Requested { get; set; }

    /// <summary>Gets or sets its name, such as Season 1 or a name of its own.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets its poster, when TMDb has one.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets when it first aired (or will), when known.</summary>
    public System.DateTime? AirDate { get; set; }
}
