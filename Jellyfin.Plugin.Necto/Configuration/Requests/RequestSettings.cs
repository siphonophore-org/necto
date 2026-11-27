using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests.Models;

namespace Jellyfin.Plugin.Necto.Configuration.Requests;

/// <summary>
/// The Requests settings. Who may request, and how much, is set on groups.
/// </summary>
public class RequestSettings
{
    /// <summary>Gets or sets a value indicating whether Requests is on: the request page, its routes, its tasks, and its menu link.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many days a movie quota counts requests back over.</summary>
    public int MovieQuotaDays { get; set; } = 7;

    /// <summary>Gets or sets how many days a series quota counts requests back over.</summary>
    public int SeriesQuotaDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets how many days declined and available requests are kept before the cleanup task removes
    /// them. 0 keeps them forever.
    /// </summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Gets or sets what Jellyfin scans once Radarr or Sonarr has new files for an open request.</summary>
    public LibraryRefresh RefreshOnAvailable { get; set; } = LibraryRefresh.ItemFolder;

    /// <summary>
    /// Gets or sets age ratings (such as G, PG, TV-Y) whose titles are approved as soon as anyone requests
    /// them. Empty approves nothing by rating.
    /// </summary>
    public List<string> AutoApproveRatings { get; set; } = new();

    /// <summary>Gets or sets after how many days a request still waiting for approval is declined. 0 never declines.</summary>
    public int AutoDeclineAfterDays { get; set; }

    /// <summary>Gets or sets the reason given to requesters when a request is declined for waiting too long.</summary>
    public string AutoDeclineReason { get; set; } = "Not approved in time. Request it again if you still want it.";

    /// <summary>
    /// Gets or sets after how many days a request still waiting for approval is brought to administrators' attention,
    /// once. 0 never does.
    /// </summary>
    public int AgingAfterDays { get; set; }

    /// <summary>Gets or sets titles that can't be requested, picked from the request page.</summary>
    public List<BlockedTitle> BlockedTitles { get; set; } = new();

    /// <summary>Gets or sets genres whose titles can't be requested, such as Reality or Horror.</summary>
    public List<string> BlockedGenres { get; set; } = new();

    /// <summary>Gets or sets age ratings whose titles can't be requested, such as NC-17 or TV-MA.</summary>
    public List<string> BlockedRatings { get; set; } = new();

    /// <summary>Gets or sets words or phrases that block any title containing them.</summary>
    public List<string> BlockedKeywords { get; set; } = new();

    /// <summary>Gets or sets TMDb keywords whose titles can't be requested, such as anime.</summary>
    public List<TmdbName> BlockedTags { get; set; } = new();

    /// <summary>Gets or sets original languages whose titles can't be requested, as ISO 639-1 codes such as ja.</summary>
    public List<string> BlockedLanguages { get; set; } = new();

    /// <summary>Gets or sets TMDb collections whose movies can't be requested, such as Dune Collection.</summary>
    public List<TmdbName> BlockedCollections { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the Requests link has been added to the web client's menu, or found
    /// there already, since Requests was turned on. It is added once, so one removed by hand stays removed until
    /// Requests is turned off and on.
    /// </summary>
    public bool MenuLinkAdded { get; set; }

    /// <summary>Gets or sets the name the Requests link is added with, as set in Menu links. Empty is Requests.</summary>
    public string MenuLinkName { get; set; } = string.Empty;

    /// <summary>Gets or sets the Material icon the Requests link is added with, as set in Menu links. Empty is search.</summary>
    public string MenuLinkIcon { get; set; } = string.Empty;

    /// <summary>Gets or sets the Radarr and Sonarr servers approved requests are sent to, each with its profiles.</summary>
    public List<ArrServer> ArrServers { get; set; } = new();

    /// <summary>
    /// Gets or sets a TMDb API key of the server's own for discovery (trending, upcoming, browsing, More Like This),
    /// encrypted. Empty uses Jellyfin's.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets home rows of the server's own, after the built-in ones: TMDb's titles with a genre, keyword, studio, network or streaming service.</summary>
    public List<HomeRow> HomeRows { get; set; } = new();

    /// <summary>
    /// Gets or sets the order of the home rows, built-in and the server's own, by key (TrendingShows, Custom-id). Rows
    /// left out follow in their usual order.
    /// </summary>
    public List<string> HomeRowOrder { get; set; } = new();

    /// <summary>Gets or sets built-in home rows not shown, by key, such as UpcomingShows.</summary>
    public List<string> HiddenHomeRows { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the home rows and their pages leave out titles the library already has all of.</summary>
    public bool HideAvailableInRows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a show can only be requested whole: no picking seasons. A show the
    /// library has part of is requested for every season it's missing.
    /// </summary>
    public bool FullSeriesOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a show's specials can be requested, each on its own.
    /// </summary>
    public bool AllowSpecials { get; set; }
}

/// <summary>Something TMDb knows by id, with its name for the settings to show.</summary>
public class TmdbName
{
    /// <summary>Gets or sets its TMDb id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A home row of the server's own: TMDb's movies or shows with any of some genres, keywords, studios, networks or
/// streaming services, in an order.
/// </summary>
public class HomeRow
{
    /// <summary>What a row's titles can be picked by.</summary>
    public static readonly string[] Kinds = ["Genre", "Keyword", "Company", "Network", "Provider"];

    /// <summary>Gets or sets the row's id, which its address carries.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets its heading.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets whether it lists movies or shows.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets what its titles are picked by: one of <see cref="Kinds"/>.</summary>
    public string Kind { get; set; } = "Genre";

    /// <summary>Gets or sets the TMDb ids of what they're picked by, separated by commas; any of them counts.</summary>
    public string Ids { get; set; } = string.Empty;

    /// <summary>Gets or sets the order: empty for most popular first, else TopRated, Newest, Oldest or Title.</summary>
    public string Sort { get; set; } = string.Empty;

    /// <summary>Gets or sets the names of what it's picked by, in the order of <see cref="Ids"/>, separated by |, for the settings to show.</summary>
    public string Names { get; set; } = string.Empty;
}

/// <summary>
/// A title that can't be requested.
/// </summary>
public class BlockedTitle
{
    /// <summary>Gets or sets whether it is a movie or a show.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets its title, for the settings page.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets its year, for the settings page.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets its TMDb id, when known.</summary>
    public string? Tmdb { get; set; }

    /// <summary>Gets or sets its TVDb id, when known.</summary>
    public string? Tvdb { get; set; }

    /// <summary>Gets or sets its IMDb id, when known.</summary>
    public string? Imdb { get; set; }
}
