using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One episode of a season, as TMDb lists it, and whether the library has it.
/// </summary>
public class EpisodeInfo
{
    /// <summary>Gets or sets its number in the season.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets its name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets what happens in it.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets when it aired, or will; null when not yet known.</summary>
    public DateTime? AirDate { get; set; }

    /// <summary>Gets or sets a still from it, when TMDb has one.</summary>
    public string? StillUrl { get; set; }

    /// <summary>Gets or sets its running time, in minutes, when known.</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>Gets or sets a value indicating whether the library has it, for the viewer.</summary>
    public bool InLibrary { get; set; }
}

/// <summary>
/// A season of a show and its episodes, as TMDb lists them.
/// </summary>
public class SeasonEpisodes
{
    /// <summary>Gets or sets the season's name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets what the season is about.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets its poster.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets its episodes, in order.</summary>
    public List<EpisodeInfo> Episodes { get; set; } = new();
}
