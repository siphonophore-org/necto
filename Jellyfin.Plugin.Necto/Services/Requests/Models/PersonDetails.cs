using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A person's page: who they are and what of theirs the library has.
/// </summary>
public class PersonDetails
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the biography.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the birth date.</summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>Gets or sets the death date.</summary>
    public DateTime? DeathDate { get; set; }

    /// <summary>Gets or sets the birthplace.</summary>
    public string? Birthplace { get; set; }

    /// <summary>Gets or sets a photo address.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Gets or sets the person's TMDb id, when known.</summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin's own id for the person when the library has them in something the viewer may see, so the
    /// page opens their Jellyfin page instead; null otherwise.
    /// </summary>
    public Guid? LibraryPersonId { get; set; }

    /// <summary>Gets or sets their movies and shows in the library, newest first.</summary>
    public List<SearchResult> LibraryTitles { get; set; } = new();

    /// <summary>Gets or sets the rest of what they're in or worked on, from TMDb, most popular first: titles the library doesn't have for the viewer, marked as search results are.</summary>
    public List<SearchResult> Credits { get; set; } = new();
}
