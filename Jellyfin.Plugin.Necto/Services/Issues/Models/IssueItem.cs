using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// A playable library item as the issue page shows it: its name, what it is, and where its pictures come from.
/// </summary>
public class IssueItem
{
    /// <summary>Gets or sets the library item's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets its Jellyfin type, such as Movie, Episode, Video, MusicVideo, Audio or AudioBook.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets its own name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets how it reads in a list, such as "Show · S1E2" for an episode.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets an episode's show, or a song's album artist.</summary>
    public string? ParentName { get; set; }

    /// <summary>Gets or sets an episode's show's id, for its poster.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Gets or sets an episode's season number.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets an episode's number.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets its year, when known.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets its primary image's tag, or null when it has none.</summary>
    public string? ImageTag { get; set; }

    /// <summary>Gets or sets its primary image's width over its height, when known.</summary>
    public double? ImageAspectRatio { get; set; }

    /// <summary>Gets or sets an episode's show's primary image tag, for a poster in place of the episode's own picture.</summary>
    public string? SeriesImageTag { get; set; }
}
