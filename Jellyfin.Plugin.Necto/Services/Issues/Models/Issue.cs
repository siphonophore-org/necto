using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// A problem with one playable library item, reported by one user and joined by others. It keeps a snapshot of the
/// item, so it reads the same after the item is gone.
/// </summary>
public class Issue
{
    /// <summary>Gets or sets the issue's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the library item it is about.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the item's Jellyfin type, such as Movie, Episode, Video or MusicVideo.</summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>Gets or sets the item's name, as it was when reported.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets an episode's show, as it was when reported.</summary>
    public string? SeriesName { get; set; }

    /// <summary>Gets or sets an episode's show's library id, for its poster.</summary>
    public Guid? SeriesId { get; set; }

    /// <summary>Gets or sets an episode's season number.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Gets or sets an episode's number.</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Gets or sets the item's year, when known.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the item's file, as it was when reported.</summary>
    public string? Path { get; set; }

    /// <summary>Gets or sets the file's size in bytes, as it was when reported.</summary>
    public long? FileSize { get; set; }

    /// <summary>Gets or sets when the file last changed, as it was when reported.</summary>
    public DateTime? FileModifiedUtc { get; set; }

    /// <summary>Gets or sets the item's provider ids (Tmdb, Tvdb, Imdb).</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets an episode's show's provider ids, which Sonarr knows the show by.</summary>
    public Dictionary<string, string>? SeriesProviderIds { get; set; }

    /// <summary>Gets or sets where it stands.</summary>
    public IssueState State { get; set; }

    /// <summary>Gets or sets who reported it, the first reporter first, then those who joined, each with what they say is wrong.</summary>
    public List<IssueReporter> Reporters { get; set; } = new();

    /// <summary>Gets or sets the notes, in the order they were written.</summary>
    public List<IssueNote> Notes { get; set; } = new();

    /// <summary>Gets or sets when it was reported.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when it last changed.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Gets or sets a value indicating whether the item's file was replaced or rescanned since it was reported.</summary>
    public bool FileChanged { get; set; }

    /// <summary>Gets or sets the administrator who last decided it.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Gets a value indicating whether it is still open: Reported or Confirmed.</summary>
    [JsonIgnore]
    public bool IsOpen => IsOpenState(State);

    /// <summary>Gets the first reporter, or null for an issue with none left.</summary>
    [JsonIgnore]
    public IssueReporter? FirstReporter => Reporters.FirstOrDefault();

    /// <summary>Gets whether a state is open.</summary>
    /// <param name="state">The state.</param>
    /// <returns><c>true</c> for Reported and Confirmed.</returns>
    public static bool IsOpenState(IssueState state) => state is IssueState.Reported or IssueState.Confirmed;

    /// <summary>Gets whether someone reported this issue or joined it.</summary>
    /// <param name="userId">The user.</param>
    /// <returns><c>true</c> when they are among its reporters.</returns>
    public bool HasReporter(Guid userId) => Reporters.Any(r => r.UserId == userId);
}
