using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One user's request for a movie or show.
/// </summary>
public class MediaRequest
{
    /// <summary>Gets or sets the request id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets what was requested.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the release year, when known.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the overview, when known.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the poster address from the metadata provider, when known.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets a wide background image address, when known, for the request's details page.</summary>
    public string? BackdropUrl { get; set; }

    /// <summary>Gets or sets the provider ids (Tmdb, Imdb, Tvdb) the title is matched by.</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the seasons asked for, or null for a whole show (and for every movie).</summary>
    public List<int>? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the specials asked for, when the request is for specials rather than seasons: <see cref="Seasons"/>
    /// is then season 0 alone. Null for any other request.
    /// </summary>
    public List<SpecialEpisode>? Specials { get; set; }

    /// <summary>Gets or sets the requester's user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the requester's name, kept for when the account is gone.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets what the requester wrote with the request, if anything.</summary>
    public string? RequesterNote { get; set; }

    /// <summary>Gets or sets when the request was made.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when the request last changed.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Gets or sets where the request stands.</summary>
    public RequestStatus Status { get; set; }

    /// <summary>Gets or sets the reason for a decline, or what went wrong for a failure.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets the name of the administrator who approved or declined it.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Gets or sets when it was approved or declined; null before, and for requests decided before this was kept.</summary>
    public DateTime? DecidedUtc { get; set; }

    /// <summary>Gets or sets the people who joined the request, wanting the title too, in the order they joined; null for none.</summary>
    public List<RequestJoiner>? Joined { get; set; }

    /// <summary>Gets or sets where each requested season of a show stands in Sonarr, as last read; null before then.</summary>
    public List<SeasonProgress>? SeasonProgress { get; set; }

    /// <summary>Gets or sets the title's genres as TMDb names them, for filtering by; null until they're known.</summary>
    public List<string>? Genres { get; set; }

    /// <summary>Gets or sets the library item the request was fulfilled by.</summary>
    public Guid? LibraryItemId { get; set; }

    /// <summary>Gets or sets the seasons of a show request already in the library.</summary>
    public List<int>? AvailableSeasons { get; set; }

    /// <summary>
    /// Gets or sets how many episodes each season of a show has, by season number, as Sonarr last reported
    /// them. A season counts as in the library only once it has that many; null when Sonarr was never asked.
    /// </summary>
    public Dictionary<int, int>? ExpectedEpisodes { get; set; }

    /// <summary>Gets or sets the movie or series id in Radarr or Sonarr once it was added there.</summary>
    public int? ArrId { get; set; }

    /// <summary>Gets or sets the Radarr or Sonarr server the request goes to, or null for the first that takes its type.</summary>
    public string? ServerId { get; set; }

    /// <summary>Gets or sets the profile on that server the title is added with, or null for the server's first.</summary>
    public string? ProfileId { get; set; }

    /// <summary>
    /// Gets or sets what the request counted against its quota when it was made: 1 for a movie, its seasons for a
    /// show. Null for requests made before it was recorded.
    /// </summary>
    public int? QuotaCost { get; set; }

    /// <summary>Gets or sets the download progress from 0 to 1, while Radarr or Sonarr is downloading it.</summary>
    public double? Progress { get; set; }

    /// <summary>Gets or sets what Radarr or Sonarr is doing with it, in words.</summary>
    public string? ProgressText { get; set; }

    /// <summary>Gets or sets how many times in a row sending it to Radarr or Sonarr failed.</summary>
    public int SendFailures { get; set; }

    /// <summary>Gets or sets when a failed send is retried next.</summary>
    public DateTime? RetryAfterUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Radarr or Sonarr no longer has the title it was sent: removed there after
    /// the send, it keeps its id so it isn't sent again (the administrator may have removed it on purpose). Such a
    /// request no longer holds a place: it doesn't block the same title for others, doesn't count toward its requester's
    /// open requests, and its requester may remove it. Cleared once the title is back.
    /// </summary>
    public bool RemovedFromArr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an administrator marked it available by hand, so the library checks
    /// leave it so even when they find no copy.
    /// </summary>
    public bool MarkedAvailable { get; set; }

    /// <summary>Gets or sets when its download should be done, as the download client last estimated it; null when it didn't say.</summary>
    public DateTime? DownloadDoneUtc { get; set; }

    /// <summary>
    /// Gets or sets when administrators were told the request has waited long for approval, so they are told only once.
    /// Null until then.
    /// </summary>
    public DateTime? AgingNotifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the days left before automatic rejection that administrators were last told of (3, 2 or 1), so a
    /// restart or a second check the same day doesn't tell them again. Null until the first.
    /// </summary>
    public int? ExpiringNotifiedDays { get; set; }

    /// <summary>Gets a value indicating whether the request is still waiting on something.</summary>
    public bool IsOpen => Status is RequestStatus.Pending or RequestStatus.Approved or RequestStatus.Failed or RequestStatus.PartiallyAvailable;

    /// <summary>Gets a value indicating whether Radarr or Sonarr is expected to be working on it.</summary>
    public bool IsInFlight => Status is RequestStatus.Approved or RequestStatus.PartiallyAvailable;

    /// <summary>
    /// Gets a value indicating whether the request holds a place: open, and not one Radarr or Sonarr has dropped. Only
    /// these block the same title for others and count toward the requester's open requests.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HoldsAPlace => IsOpen && !RemovedFromArr;
}
