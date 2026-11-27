using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// What a list of titles is narrowed to, as the web client's library filters narrow a library: whether the library has
/// them, where they stand with requests, movies or shows, and genres. Within each, any choice matches; across them, all
/// must. Nothing chosen leaves a list as it is.
/// </summary>
public sealed class TitleFilter
{
    /// <summary>Not requested, nor in Radarr or Sonarr.</summary>
    public const string NotRequested = "NotRequested";

    /// <summary>Requested, waiting for approval.</summary>
    public const string Requested = "Requested";

    /// <summary>Requested and approved, not yet downloaded.</summary>
    public const string Approved = "Approved";

    /// <summary>In Radarr or Sonarr already, with no request.</summary>
    public const string Monitored = "Monitored";

    /// <summary>Downloaded, waiting for the library to scan it in. For administrators, also a show Sonarr has some of.</summary>
    public const string Downloaded = "Downloaded";

    /// <summary>Its request was rejected.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Gets every state a title can be in, in the order the filters list them.</summary>
    public static IReadOnlyList<string> States { get; } = [NotRequested, Requested, Approved, Monitored, Downloaded];

    /// <summary>Gets or sets whether titles must be in the library (true) or not (false); null for either.</summary>
    public bool? InLibrary { get; set; }

    /// <summary>Gets the states a title must be in, one of; empty for any.</summary>
    public HashSet<string> StatesWanted { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the types a title must be, one of; empty for either.</summary>
    public HashSet<RequestType> Types { get; } = new();

    /// <summary>Gets the genres a title must have, one of; empty for any.</summary>
    public HashSet<string> Genres { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the sorts a list can be read in, besides TMDb's own order (most popular first).</summary>
    public static IReadOnlyList<string> Sorts { get; } = ["TopRated", "Newest", "Oldest", "Title"];

    /// <summary>Gets the runtimes a title can be filtered by: under 90 minutes, 90 to 120, over two hours.</summary>
    public static IReadOnlyList<string> Runtimes { get; } = ["Short", "Medium", "Long"];

    /// <summary>Gets or sets the order a list TMDb discovers is read in (one of <see cref="Sorts"/>); null for most popular first.</summary>
    public string? Sort { get; set; }

    /// <summary>Gets the decades a title must be from, one of, by their first year (1990 for the 1990s, 1900 for anything before 1970); empty for any.</summary>
    public HashSet<int> Decades { get; } = new();

    /// <summary>Gets or sets the least TMDb rating a title must have, out of ten; null for any.</summary>
    public int? MinRating { get; set; }

    /// <summary>Gets the runtimes a title must be, one of (see <see cref="Runtimes"/>); empty for any. Only lists TMDb discovers can be narrowed by them.</summary>
    public HashSet<string> RuntimesWanted { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the original languages a title must be in, one of, as ISO 639-1 codes; empty for any.</summary>
    public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the streaming services, by TMDb's ids, a title must be on, one of, in the server's country; empty for any. Only lists TMDb discovers can be narrowed by them.</summary>
    public HashSet<int> Providers { get; } = new();

    /// <summary>Gets a value indicating whether it leaves every title in (in whatever order).</summary>
    public bool IsEmpty => InLibrary is null && StatesWanted.Count == 0 && Types.Count == 0 && Genres.Count == 0
        && Decades.Count == 0 && MinRating is null && RuntimesWanted.Count == 0 && Languages.Count == 0 && Providers.Count == 0;

    /// <summary>Gets a value indicating whether TMDb has anything to do for a list it discovers: an order, or a filter it narrows by.</summary>
    public bool AsksTmdb => Sort is not null || Decades.Count > 0 || MinRating is not null || RuntimesWanted.Count > 0 || Languages.Count > 0 || Providers.Count > 0;

    /// <summary>
    /// Reads the parts of a filter only lists TMDb discovers use: its order, decades, rating, runtimes, languages and
    /// streaming services. Anything it doesn't know is left out.
    /// </summary>
    /// <param name="sort">One of <see cref="Sorts"/>.</param>
    /// <param name="decades">Decades by first year, separated by commas, such as 1990,2000.</param>
    /// <param name="minRating">The least rating, 1 to 9.</param>
    /// <param name="runtimes">Runtimes, separated by commas: Short, Medium, Long.</param>
    /// <param name="languages">ISO 639-1 codes, separated by commas.</param>
    /// <param name="providers">TMDb streaming service ids, separated by commas.</param>
    /// <returns>This filter.</returns>
    public TitleFilter WithDiscover(string? sort, string? decades, int? minRating, string? runtimes, string? languages, string? providers)
    {
        Sort = Sorts.FirstOrDefault(s => string.Equals(s, sort, StringComparison.OrdinalIgnoreCase));
        Decades.UnionWith(Split(decades, ',').Select(d => int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ? year : 0)
            .Where(y => y == 1900 || (y >= 1970 && y <= 2100 && y % 10 == 0)).Take(20));
        MinRating = minRating is >= 1 and <= 9 ? minRating : null;
        RuntimesWanted.UnionWith(Split(runtimes, ',').Select(r => Runtimes.FirstOrDefault(k => string.Equals(k, r, StringComparison.OrdinalIgnoreCase))).OfType<string>());
        Languages.UnionWith(Split(languages, ',').Where(l => l.Length == 2 && l.All(char.IsAsciiLetterLower)).Take(20));
        Providers.UnionWith(Split(providers, ',').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0).Take(20));
        return this;
    }

    /// <summary>
    /// Gets what TMDb's discover is asked with for this filter's order and the filters it narrows by: a list it
    /// discovers is then sorted and narrowed by TMDb itself, so its pages are as full as unfiltered.
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="region">The server's country, for streaming services.</param>
    /// <returns>The parameters.</returns>
    public Dictionary<string, string> DiscoverQuery(RequestType type, string region)
    {
        var movie = type == RequestType.Movie;
        var date = movie ? "primary_release_date" : "first_air_date";
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        switch (Sort)
        {
            case "TopRated":
                query["sort_by"] = "vote_average.desc";
                query["vote_count.gte"] = "200";
                break;
            case "Newest":
                // Out already, newest first: not what's announced for years ahead.
                query["sort_by"] = date + ".desc";
                query[date + ".lte"] = today;
                break;
            case "Oldest":
                query["sort_by"] = date + ".asc";
                break;
            case "Title":
                query["sort_by"] = movie ? "title.asc" : "name.asc";
                break;
        }

        if (Decades.Count > 0)
        {
            query[date + ".gte"] = (Decades.Min() == 1900 ? 1874 : Decades.Min()).ToString(CultureInfo.InvariantCulture) + "-01-01";
            var last = Decades.Max() == 1900 ? 1969 : Decades.Max() + 9;
            var lte = last.ToString(CultureInfo.InvariantCulture) + "-12-31";
            query[date + ".lte"] = query.TryGetValue(date + ".lte", out var already) && string.CompareOrdinal(already, lte) < 0 ? already : lte;
        }

        if (MinRating is { } rating)
        {
            query["vote_average.gte"] = rating.ToString(CultureInfo.InvariantCulture);
            query["vote_count.gte"] = query.TryGetValue("vote_count.gte", out var votes) ? votes : "50";
        }

        if (RuntimesWanted.Count > 0)
        {
            query["with_runtime.gte"] = RuntimesWanted.Contains("Short") ? "1" : RuntimesWanted.Contains("Medium") ? "90" : "121";
            if (!RuntimesWanted.Contains("Long"))
            {
                query["with_runtime.lte"] = RuntimesWanted.Contains("Medium") ? "120" : "89";
            }
        }

        if (Languages.Count > 0)
        {
            query["with_original_language"] = string.Join('|', Languages.Order(StringComparer.Ordinal));
        }

        if (Providers.Count > 0)
        {
            query["with_watch_providers"] = string.Join('|', Providers.Order());
            query["watch_region"] = region;
        }

        return query;
    }

    /// <summary>
    /// Reads a filter from a request's query, in Jellyfin's style: states and types separated by commas, genres by |, as
    /// the web client sends Genres. Anything it doesn't know is left out.
    /// </summary>
    /// <param name="inLibrary">True for only titles in the library, false for only those not in it.</param>
    /// <param name="states">States, such as NotRequested,Requested.</param>
    /// <param name="types">Movie, Series or both.</param>
    /// <param name="genres">Genres, such as Drama|Comedy.</param>
    /// <returns>The filter.</returns>
    public static TitleFilter Parse(bool? inLibrary, string? states, string? types, string? genres)
    {
        var filter = new TitleFilter { InLibrary = inLibrary };
        foreach (var state in Split(states, ','))
        {
            if (States.FirstOrDefault(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase)) is { } known)
            {
                filter.StatesWanted.Add(known);
            }
        }

        foreach (var type in Split(types, ','))
        {
            if (Enum.TryParse<RequestType>(type, true, out var parsed) && Enum.IsDefined(parsed))
            {
                filter.Types.Add(parsed);
            }
        }

        filter.Genres.UnionWith(GenreNames(genres));
        return filter;
    }

    /// <summary>Gets where a title stands with requests, as its card's badge says.</summary>
    /// <param name="title">The title, marked as search marks it.</param>
    /// <param name="seesArrFiles">Whether the viewer is told what Radarr and Sonarr have files for (administrators): their
    /// cards call a show Sonarr has some of Partly Downloaded, where others' call it on its way.</param>
    /// <returns>One of <see cref="States"/>.</returns>
    public static string StateOf(SearchResult title, bool seesArrFiles = false)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.RequestStatus switch
        {
            RequestStatus.Declined => Rejected,
            // Waiting for approval comes first, as the card says it does.
            RequestStatus.Pending => Requested,
            // Others see Downloaded only as search marks it (a movie in a library they can't see isn't).
            _ when title.Downloaded || (title.ArrHasFiles && seesArrFiles) => Downloaded,
            RequestStatus.Approved or RequestStatus.Failed or RequestStatus.PartiallyAvailable => Approved,
            _ when title.InArr => Monitored,
            _ => NotRequested
        };
    }

    /// <summary>Gets whether a title is in the library: all of it, as a show only partly there can still be requested.</summary>
    /// <param name="title">The title.</param>
    /// <returns>True when it is.</returns>
    public static bool IsInLibrary(SearchResult title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.LibraryItemId is not null && !title.Partial;
    }

    /// <summary>Gets whether a title, marked as search marks it, passes the filter.</summary>
    /// <param name="title">The title.</param>
    /// <param name="seesArrFiles">Whether the viewer is told what Radarr and Sonarr have files for (see <see cref="StateOf"/>).</param>
    /// <returns>True when it does.</returns>
    public bool Matches(SearchResult title, bool seesArrFiles = false)
    {
        ArgumentNullException.ThrowIfNull(title);
        return MatchesTitle(title)
            && (InLibrary is not { } library || IsInLibrary(title) == library)
            && (StatesWanted.Count == 0 || StatesWanted.Contains(StateOf(title, seesArrFiles)));
    }

    /// <summary>
    /// Gets whether a title passes the parts of the filter its own details settle (type and genres), before it's looked
    /// up in the library, Radarr or Sonarr: a title that fails these needn't be.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>True when it does.</returns>
    public bool MatchesTitle(SearchResult title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var year = title.ReleaseDate?.Year ?? title.Year;
        return (Types.Count == 0 || Types.Contains(title.Type)) && (Genres.Count == 0 || title.Genres.Any(Genres.Contains))
            && (Decades.Count == 0 || (year is { } y && Decades.Contains(y < 1970 ? 1900 : y / 10 * 10)))
            && (MinRating is not { } least || (title.TmdbRating ?? title.CommunityRating) >= least)
            && (Languages.Count == 0 || (title.Language is { } language && Languages.Contains(language)));
    }

    /// <summary>
    /// Reads genres separated by |, as Jellyfin's Genres are: each at most 64 characters and at most 20 of them, as no
    /// real filter needs more, and each becomes part of a database query.
    /// </summary>
    /// <param name="genres">The genres, such as Drama|Comedy.</param>
    /// <returns>The genres, each once.</returns>
    internal static IEnumerable<string> GenreNames(string? genres)
        => Split(genres, '|').Where(g => g.Length <= 64).Distinct(StringComparer.OrdinalIgnoreCase).Take(20);

    private static string[] Split(string? value, char separator)
        => (value ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
