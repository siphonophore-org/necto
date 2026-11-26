using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One title found by a search, with whether the library has it and whether it was requested.
/// </summary>
public class SearchResult
{
    /// <summary>Gets or sets what was found.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the release year, when known.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the overview, when known.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the poster address, when known.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets the provider ids.</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the library item that already has it, or null.</summary>
    public Guid? LibraryItemId { get; set; }

    /// <summary>Gets or sets the status of an open or fulfilled request for it, or null.</summary>
    public RequestStatus? RequestStatus { get; set; }

    /// <summary>Gets or sets who made that request: their name for the viewer's own, or when the viewer may see it; else null.</summary>
    public string? RequestedBy { get; set; }

    /// <summary>Gets or sets the id of that request, so the viewer can cancel it while it waits for approval.</summary>
    public Guid? RequestId { get; set; }

    /// <summary>Gets or sets a value indicating whether the viewer joined that request, someone else's.</summary>
    public bool Joined { get; set; }

    /// <summary>Gets or sets a value indicating whether the viewer may cancel that request: their own while pending, or any as an administrator.</summary>
    public bool CanCancel { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a show that is only partly in the library or partly
    /// requested, so more of its seasons may still be requested.
    /// </summary>
    public bool Partial { get; set; }

    /// <summary>Gets or sets a value indicating whether a movie is already downloaded and on its way to the library, for everyone.</summary>
    public bool Downloaded { get; set; }

    /// <summary>Gets or sets a value indicating whether the blocklist keeps this title from being requested.</summary>
    public bool Blocked { get; set; }

    /// <summary>Gets or sets why it is blocked, for administrators only.</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Gets or sets the original title, when it differs from the title.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Gets or sets a wide background image address, when known.</summary>
    public string? BackdropUrl { get; set; }

    /// <summary>Gets or sets the genres.</summary>
    public List<string> Genres { get; set; } = new();

    /// <summary>Gets or sets the running time in minutes (per episode for a show), when known.</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>Gets or sets the age rating, such as PG-13 or TV-MA.</summary>
    public string? Certification { get; set; }

    /// <summary>Gets or sets the studio of a movie or the network of a show.</summary>
    public string? Studio { get; set; }

    /// <summary>Gets or sets the TMDb user rating out of 10.</summary>
    public double? TmdbRating { get; set; }

    /// <summary>
    /// Gets or sets the community rating out of 10: the library's for titles from the library, and for a show from
    /// Sonarr its TVDb rating, so the page shows stars for either.
    /// </summary>
    public double? CommunityRating { get; set; }

    /// <summary>Gets or sets the Rotten Tomatoes critic score out of 100.</summary>
    public int? RottenTomatoes { get; set; }

    /// <summary>Gets or sets the release date of a movie or the first air date of a show.</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the year an ended show last aired, for showing its run as "2008 - 2013"; null for a movie, a show
    /// still running, or when it isn't known.
    /// </summary>
    public int? EndYear { get; set; }

    /// <summary>Gets or sets where the title stands: Released, In Cinemas, Announced, Continuing, Ended or Upcoming.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets how many seasons a show has, specials left out.</summary>
    public int? SeasonCount { get; set; }

    /// <summary>Gets or sets a trailer address on YouTube, when known.</summary>
    public string? TrailerUrl { get; set; }

    /// <summary>Gets or sets a transparent title logo, when known.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Gets or sets the original language, such as English or Japanese.</summary>
    public string? Language { get; set; }

    /// <summary>Gets or sets a value indicating whether Radarr or Sonarr already has the title.</summary>
    public bool InArr { get; set; }

    /// <summary>Gets or sets a value indicating whether Radarr already has the movie's file, or Sonarr every episode it wants.</summary>
    public bool ArrHasFiles { get; set; }

    /// <summary>
    /// Fills every detail this result lacks from another result for the same title, so a library title keeps
    /// its own artwork while gaining what Radarr or Sonarr knows.
    /// </summary>
    /// <param name="other">The other result.</param>
    public void FillFrom(SearchResult other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var id in other.ProviderIds)
        {
            ProviderIds.TryAdd(id.Key, id.Value);
        }

        Overview ??= other.Overview;
        PosterUrl ??= other.PosterUrl;
        OriginalTitle ??= other.OriginalTitle;
        BackdropUrl ??= other.BackdropUrl;
        if (Genres.Count == 0)
        {
            Genres = other.Genres;
        }

        RuntimeMinutes ??= other.RuntimeMinutes;
        Certification ??= other.Certification;
        Studio ??= other.Studio;
        TmdbRating ??= other.TmdbRating;
        CommunityRating ??= other.CommunityRating;
        RottenTomatoes ??= other.RottenTomatoes;
        ReleaseDate ??= other.ReleaseDate;
        Status ??= other.Status;
        EndYear ??= other.EndYear;
        SeasonCount ??= other.SeasonCount;
        TrailerUrl ??= other.TrailerUrl;
        LogoUrl ??= other.LogoUrl;
        Language ??= other.Language;
        InArr |= other.InArr;
        ArrHasFiles |= other.ArrHasFiles;
    }
}
