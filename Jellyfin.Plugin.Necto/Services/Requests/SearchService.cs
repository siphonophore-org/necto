using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Searches TMDb for movies and shows and reads its lists, marking each title with what the library, Radarr or Sonarr
/// and the requests already hold for the viewer.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public class SearchService
{
    // Per type, so a broad search on movies and shows together can't crowd either one out. Radarr and Sonarr
    // return one page of about 20 on their own; the rest of the room is for library matches.
    private const int MaxResultsPerKind = 30;
    private const int LibraryResults = 10;

    // Library items fetched to match remote results against, beyond those listed.
    private const int LibraryCandidates = 40;

    /// <summary>The most titles a row of the empty search holds.</summary>
    internal const int RowSize = 20;

    /// <summary>The most of TMDb's pages one read of a list looks through for titles a filter passes.</summary>
    internal const int MaxPagesPerRead = 10;

    // How many of TMDb's pages a discover row looks through: a row shows a preview, and its list page reads on.
    internal const int PreviewPages = 2;

    private readonly ArrClient _arr;
    private readonly TmdbDiscovery _tmdb;
    private readonly LibraryMatcher _library;
    private readonly RequestDatabase _db;
    private readonly ILogger<SearchService> _logger;
    private readonly ArtworkCache? _artwork;

    // When the request page was last used (searched, its rows or lists read, a title marked), and what the first use
    // after a quiet spell completes, so the warmer reads Radarr's and Sonarr's libraries again straight away.
    private long _usedTicks;
    private TaskCompletionSource _woken = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The open requests by each of their ids, for each list of them Annotate is given: the database hands out the same
    // list until a request changes, so this is built once per change rather than once per list or search.
    private static readonly ConditionalWeakTable<IReadOnlyList<MediaRequest>, RequestIndex> _indexes = new();

    /// <summary>How long after the request page was last used Radarr's and Sonarr's libraries are kept read.</summary>
    internal static readonly TimeSpan InUseFor = TimeSpan.FromMinutes(30);

    /// <summary>The empty search's rows, in order: the key the page and its list pages know each by, and its heading.</summary>
    internal static readonly (string Key, string Title)[] DiscoverKinds =
    [
        (RecentlyAddedRow, "Recently Added"),
        (RecommendedRow, "Recommended for you"),
        ("TrendingShows", "Trending shows"),
        ("TrendingMovies", "Trending movies"),
        ("UpcomingShows", "Upcoming shows"),
        ("UpcomingMovies", "Upcoming movies"),
        ("PopularShows", "Popular shows"),
        ("PopularMovies", "Popular movies")
    ];

    /// <summary>The home row of what the library added last, the viewer's to see.</summary>
    internal const string RecentlyAddedRow = "RecentlyAdded";

    /// <summary>The home row of TMDb's recommendations for what the viewer watched last.</summary>
    internal const string RecommendedRow = "RecommendedForYou";

    /// <summary>The most titles a row made for the viewer (Recently Added, Recommended for You) holds, its list page included.</summary>
    internal const int ViewerRowSize = 60;

    // How many of the viewer's latest watched titles Recommended for You asks TMDb about.
    private const int RecommendationSeeds = 8;

    /// <summary>Gets whether a home row is made for each viewer from the library, rather than read from a TMDb list.</summary>
    /// <param name="key">The row's key.</param>
    /// <returns>True for Recently Added and Recommended for You.</returns>
    internal static bool IsViewerRow(string? key) => key is RecentlyAddedRow or RecommendedRow;

    /// <summary>What a home row of the server's own is known by: Custom- and its id.</summary>
    internal const string CustomRowPrefix = "Custom-";

    // The home rows shown, in the order the settings give: the built-in ones, then the server's own, unless moved.
    private static List<(string Key, string Title)> Rows()
    {
        var (custom, order, hidden) = RequestConfig.Instance?.ReadConfiguration(c => (c.HomeRows.ToList(), c.HomeRowOrder.ToList(), c.HiddenHomeRows.ToList()))
            ?? (new List<HomeRow>(), new List<string>(), new List<string>());
        return Ordered(DiscoverKinds.Concat(custom.Select(r => (CustomRowPrefix + r.Id, r.Title))), order, hidden);
    }

    /// <summary>Puts rows in an order: those it names first, as it names them, then the rest as they were; hidden ones left out.</summary>
    /// <param name="rows">The rows, in their usual order.</param>
    /// <param name="order">Keys in the order wanted.</param>
    /// <param name="hidden">Keys not shown.</param>
    /// <returns>The rows shown, in order.</returns>
    internal static List<(string Key, string Title)> Ordered(IEnumerable<(string Key, string Title)> rows, List<string> order, IReadOnlyCollection<string> hidden)
        => rows.Where(r => !hidden.Contains(r.Key, StringComparer.Ordinal))
            .Select((r, i) => (Row: r, Place: order.IndexOf(r.Key) is var at and >= 0 ? at : order.Count + i))
            .OrderBy(r => r.Place)
            .Select(r => r.Row)
            .ToList();

    /// <summary>How far ahead the upcoming rows look.</summary>
    internal static readonly TimeSpan UpcomingWindow = TimeSpan.FromDays(180);

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchService"/> class.
    /// </summary>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="library">The library matcher.</param>
    /// <param name="db">The request database.</param>
    /// <param name="tmdb">TMDb's lists, through Jellyfin's TMDb access.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="artwork">The kept TMDb images, for keeping the home rows' posters ahead of the first visit.</param>
    public SearchService(ArrClient arr, LibraryMatcher library, RequestDatabase db, TmdbDiscovery tmdb, ILogger<SearchService> logger, ArtworkCache? artwork = null)
    {
        _artwork = artwork;
        _arr = arr;
        _tmdb = tmdb;
        _library = library;
        _db = db;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the request page was used lately (within InUseFor).</summary>
    internal bool InUse => DateTime.UtcNow.Ticks - Interlocked.Read(ref _usedTicks) < InUseFor.Ticks;

    /// <summary>Gets a task that completes when the request page is next used after a quiet spell.</summary>
    internal Task Woken => Volatile.Read(ref _woken).Task;

    // Set while PrimeAsync reads the rows, and in all it starts, so that read isn't taken for the page being used.
    private static readonly AsyncLocal<bool> _priming = new();

    // Notes that the request page is being used; the first use after a quiet spell wakes whoever waits on Woken.
    internal void Used()
    {
        if (_priming.Value)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Exchange(ref _usedTicks, now) >= InUseFor.Ticks)
        {
            Interlocked.Exchange(ref _woken, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        }
    }

    /// <summary>Reads a search for an id, such as tmdb:603, imdb:tt0133093 or tvdb:81189, as provider ids; null for any other search.</summary>
    /// <param name="query">The search.</param>
    /// <returns>The ids, or null.</returns>
    internal static Dictionary<string, string>? IdSearch(string query)
    {
        var match = System.Text.RegularExpressions.Regex.Match(query ?? string.Empty, @"^\s*(tmdb|imdb|tvdb)\s*:\s*(\d{1,10}|tt\d{1,10})\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var source = match.Groups[1].Value.ToLowerInvariant();
        var value = match.Groups[2].Value.ToLowerInvariant();
        if ((source == "imdb") != value.StartsWith("tt", StringComparison.Ordinal))
        {
            return null;
        }

        var ids = ProviderIds.Clean(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [source switch { "tmdb" => ProviderIds.Tmdb, "imdb" => ProviderIds.Imdb, _ => ProviderIds.Tvdb }] = value
        });
        return ids.Count > 0 ? ids : null;
    }

    /// <summary>
    /// Searches for movies, shows, or both, and marks each result with what the library and the requests already hold.
    /// </summary>
    /// <param name="query">The title to search for.</param>
    /// <param name="types">The types to search.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="viewer">Who is searching, so each result says whether they may cancel its request.</param>
    /// <returns>The results, movies and shows interleaved in provider order.</returns>
    public async Task<List<SearchResult>> SearchAsync(string query, IReadOnlyCollection<RequestType> types, CancellationToken cancellationToken, UserAccess? viewer = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0 || types.Count == 0)
        {
            return new List<SearchResult>();
        }

        Used();

        // tmdb:603, imdb:tt0133093 or tvdb:81189 finds the title with that id, of each type searched.
        if (IdSearch(query) is { } byId && viewer is not null)
        {
            var found = await Task.WhenAll(types.Select(t => TitleAsync(t, byId, viewer, cancellationToken))).ConfigureAwait(false);
            return found.OfType<SearchResult>().ToList();
        }

        var movies = types.Contains(RequestType.Movie) ? SearchKindAsync(RequestType.Movie, query, cancellationToken) : Task.FromResult(new List<SearchResult>());
        var shows = types.Contains(RequestType.Series) ? SearchKindAsync(RequestType.Series, query, cancellationToken) : Task.FromResult(new List<SearchResult>());

        var user = viewer?.User;

        // What the library already has comes first, straight from the library, so it shows as owned even when
        // Jellyfin never matched it to the ids the outside search returns. Only what the viewer may see counts. It's
        // searched while the outside searches are under way.
        var searchingLibrary = Task.Run(() => _library.Search(query, types, LibraryCandidates, user)
            .Where(item => LibraryMatcher.KindOf(item).HasValue)
            .ToList(), cancellationToken);
        var remote = Interleave(await movies.ConfigureAwait(false), await shows.ConfigureAwait(false)).ToList();
        var requests = _db.OpenRequests();

        // Which titles Radarr or Sonarr have, from every server, as the lists say it.
        // Whether a movie they have has its file yet is asked of them as it's searched, as a library read a few minutes
        // old may not know (Downloaded is only ever said of movies). Each answer is kept a few minutes (ArrClient.OwnedFor),
        // so a search typed a letter at a time asks once, not at every letter: a file that has come since shows once
        // the answer kept lapses.
        var held = await Task.WhenAll(remote.GroupBy(r => r.Type).Select(async g =>
        {
            var titles = g.ToList();
            return (Titles: titles, Held: await HeldAsync(g.Key, titles, requests, cancellationToken).ConfigureAwait(false));
        })).ConfigureAwait(false);
        foreach (var (titles, owned) in held)
        {
            var waiting = titles.Where(t => t.Type == RequestType.Movie && IdsOf(t.Type, new[] { t }).Any(id => owned.Ids.Contains(id) && !owned.WithFiles.Contains(id))).ToList();
            if (waiting.Count > 0)
            {
                // Waited on for a moment only: a slow server leaves the library read's answer.
                var asking = _arr.HeldAsync(waiting[0].Type, IdsOf(waiting[0].Type, waiting), CancellationToken.None, live: true);
                _ = asking.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                if (await Task.WhenAny(asking, Task.Delay(QuickWait, cancellationToken)).ConfigureAwait(false) == asking && asking.IsCompletedSuccessfully)
                {
                    owned.WithFiles.UnionWith((await asking.ConfigureAwait(false)).WithFiles);
                }
            }

            titles.ForEach(t => MarkArr(t, owned));
        }

        var candidates = await searchingLibrary.ConfigureAwait(false);
        var items = candidates.ToDictionary(i => i.Id);
        var results = candidates
            .Take(LibraryResults)
            .Select(item => FromLibrary(item, LibraryMatcher.KindOf(item)!.Value))
            .ToList();

        // Each candidate's ids cleaned once, not once for every result it's compared with.
        var candidateIds = candidates.ToDictionary(i => i, i => ProviderIds.Clean(i.ProviderIds), ReferenceEqualityComparer.Instance as IEqualityComparer<BaseItem>);
        foreach (var result in remote)
        {
            // Matched against the library items the search already found, by id or by title and year; only
            // a title none of them is costs a lookup, and that one by id alone.
            var owned = candidates.FirstOrDefault(i => LibraryMatcher.KindOf(i) == result.Type && ProviderIds.Overlap(candidateIds[i], result.ProviderIds))
                ?? (result.Year is { } year
                    ? candidates.FirstOrDefault(i => LibraryMatcher.KindOf(i) == result.Type
                        && new[] { result.Title, result.OriginalTitle }.OfType<string>().Any(t => LibraryMatcher.IsTitleMatch(i, result.ProviderIds, t, year)))
                    : null)
                ?? _library.Find(result.Type, result.ProviderIds, user: user);
            if (owned is not null)
            {
                items.TryAdd(owned.Id, owned);
            }

            result.LibraryItemId = owned?.Id;
        }

        // A show's seasons, for whether one in the library still has some to request: TMDb's search doesn't say. Only
        // those the viewer's library has, as just matched.
        await FillSeasonCountsAsync(remote, new WaitBudget(), cancellationToken).ConfigureAwait(false);
        foreach (var result in remote)
        {
            var same = result.LibraryItemId is { } id ? results.FirstOrDefault(r => r.LibraryItemId == id) : null;
            if (same is not null)
            {
                // Already listed from the library; keep its own artwork and details, and fill in what it lacks.
                same.FillFrom(result);
                continue;
            }

            results.Add(result);
        }

        Annotate(results, requests, viewer, items);
        results = results.GroupBy(r => r.Type).SelectMany(g => g.Take(MaxResultsPerKind)).ToList();
        return results;
    }

    /// <summary>
    /// Returns the rows the empty search shows, from TMDb through Jellyfin's TMDb access, in order: Trending Shows and
    /// Trending Movies (what's trending this week), then Upcoming Shows and Upcoming Movies. Each holds its list's first
    /// page, marked for the viewer as search results are: a library title counts only when the viewer may see it, and a
    /// title the blocklist holds back isn't shown to requesters at all, and is shown marked to administrators. A row's
    /// list page reads on with ListAsync.
    /// </summary>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="filter">What the rows' titles are narrowed to, if anything.</param>
    /// <param name="libraryFilter">What Recently Added is narrowed to, when not the filter: Hide available titles leaves it whole.</param>
    /// <returns>The rows that have titles, in order; none when the viewer can't request. Only Recently Added when TMDb can't be asked.</returns>
    public async Task<List<DiscoverRow>> DiscoverAsync(UserAccess viewer, CancellationToken cancellationToken, TitleFilter? filter = null, TitleFilter? libraryFilter = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        var rows = new List<DiscoverRow>();
        if (!viewer.CanRequest)
        {
            return rows;
        }

        Used();

        // Only the rows of the types the viewer may request, and the filter picks.
        var kinds = Rows().Where(k => IsViewerRow(k.Key)
            ? (k.Key == RecentlyAddedRow || _tmdb.Available) && (viewer.Movies || viewer.Series)
            : _tmdb.Available && TmdbList(k.Key) is { } list && list.Type is var type && (type == RequestType.Movie ? viewer.Movies : viewer.Series)
                && (filter is null || filter.Types.Count == 0 || filter.Types.Contains(type))).ToList();
        var requests = _db.OpenRequests();
        // Each row on its own thread: with TMDb and Radarr and Sonarr's answers already kept, a row's work is all
        // matching to the library, which would otherwise run one row after another.
        var pages = await Task.WhenAll(kinds.Select(k => Task.Run(
            () => IsViewerRow(k.Key)
                ? ViewerListAsync(k.Key, viewer, 0, RowSize, cancellationToken, k.Key == RecentlyAddedRow ? libraryFilter ?? filter : filter, requests)
                : ListAsync(ListFor(k.Key)!, viewer, 0, RowSize, cancellationToken, filter, PreviewPages, requests),
            cancellationToken))).ConfigureAwait(false);
        for (var i = 0; i < kinds.Count; i++)
        {
            if (pages[i] is { Items.Count: > 0 } page)
            {
                rows.Add(new DiscoverRow { Key = kinds[i].Key, Title = kinds[i].Title, Items = page.Items });
            }
        }

        return rows;
    }

    /// <summary>
    /// Reads the home rows once as a viewer would, ahead of the first visit after the server starts, so that visit
    /// doesn't pay for a first read: the code's first run, and the library's and the rows' first look-ups. Not taken
    /// for the page being used, so Radarr and Sonarr aren't read again for it.
    /// </summary>
    /// <param name="viewer">Who the rows are read for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows, as DiscoverAsync gives them.</returns>
    internal Task<List<DiscoverRow>> PrimeAsync(UserAccess viewer, CancellationToken cancellationToken)
        => Unnoticed(() => DiscoverAsync(viewer, cancellationToken));

    /// <summary>Runs a read that isn't taken for the page being used, nor is anything it starts.</summary>
    /// <typeparam name="T">What the read gives.</typeparam>
    /// <param name="read">The read.</param>
    /// <returns>What it gave.</returns>
    internal static async Task<T> Unnoticed<T>(Func<Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        // Only this read and what it starts see it: an AsyncLocal set here is undone for the caller on return.
        _priming.Value = true;
        return await read().ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the first pages of the empty search's rows (from the stored copy while it's current, else TMDb), and, while
    /// the page is in use, asks Radarr and Sonarr about their titles afresh,
    /// so the answers are already kept when anyone opens the page: neither then waits on TMDb or on asking Radarr and
    /// Sonarr about each title.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task WarmAsync(CancellationToken cancellationToken)
    {
        if (!_tmdb.Available)
        {
            return;
        }

        await Task.WhenAll(Rows().Select(async kind =>
        {
            if (ListFor(kind.Key) is not { } list)
            {
                return;
            }

            for (var number = 1; number <= PreviewPages; number++)
            {
                if (await _tmdb.PageAsync(list.Type, list.Path, list.Query, number, cancellationToken, list.Row).ConfigureAwait(false) is not { } page)
                {
                    return;
                }

                // Radarr and Sonarr only while the page is in use: otherwise nobody is waiting on their answers, and
                // their libraries aren't kept read (see DiscoveryWarmer), so each title would be asked about alone.
                if (InUse)
                {
                    await _arr.HeldAsync(list.Type, IdsOf(list.Type, page.Items), cancellationToken, fresh: true).ConfigureAwait(false);
                }
            }
        })).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads Radarr's and Sonarr's whole libraries and the home rows' first pages from TMDb afresh (the built-in rows
    /// and the server's own; see TmdbDiscovery.RefreshStoreAsync), both kept in the cache folder, so the page's first
    /// visit after a quiet spell or a restart asks nothing of TMDb, Radarr or Sonarr; TMDb's answers kept before, from
    /// the page's use since the last refresh, are let go of. Then, while the page is in use,
    /// asks Radarr and Sonarr about the rows' titles, as the rows may hold new ones, keeps the posters the rows open
    /// on (see KeepPostersAsync) and lets go of every other image kept.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many titles were stored; 0 when TMDb couldn't be asked in full.</returns>
    public async Task<int> RefreshStoredAsync(CancellationToken cancellationToken)
    {
        // Radarr and Sonarr whether or not TMDb then answers: neither waits on the other. What they said of single
        // titles since the last refresh goes, as their whole libraries were just read.
        await _arr.RefreshLibrariesAsync(cancellationToken).ConfigureAwait(false);
        _arr.ClearHeld();

        // Every row of the server's own, shown or not, as the built-in ones are: one shown again is there at once.
        var own = (RequestConfig.Instance?.ReadConfiguration(c => c.HomeRows.Select(r => CustomRowPrefix + r.Id).ToList()) ?? new List<string>())
            .Where(key => ListFor(key) is not null)
            .ToList();
        var rows = DiscoverKinds.Where(k => TmdbList(k.Key) is not null).Select(k => (k.Key, TmdbList(k.Key)!)).Concat(own.Select(key => (key, ListFor(key)!)));
        var stored = await _tmdb.RefreshStoreAsync(rows, cancellationToken).ConfigureAwait(false);
        if (stored > 0)
        {
            await WarmAsync(cancellationToken).ConfigureAwait(false);
            await KeepPostersAsync(cancellationToken, clearOthers: true).ConfigureAwait(false);
        }

        return stored;
    }

    // How many posters are read from TMDb at once ahead of time: half of ArtworkCache's places, so the page's own
    // images still have theirs.
    private const int PostersAtOnce = 4;

    /// <summary>
    /// Keeps the posters of the titles the home rows open on (their first pages, as stored) in the cache folder, so the
    /// first visit loads them from the server rather than having it read each from TMDb. One already kept is only
    /// marked as used; one that can't be kept now (the folder full, every place busy) is read as it's shown.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="clearOthers">Every other image kept is let go of: those shown since the last refresh.</param>
    /// <returns>How many posters are kept.</returns>
    internal async Task<int> KeepPostersAsync(CancellationToken cancellationToken, bool clearOthers = false)
    {
        if (_artwork is null || !_tmdb.Available)
        {
            return 0;
        }

        // Every row's, shown or hidden, as the rows themselves are stored.
        var keys = DiscoverKinds.Select(k => k.Key)
            .Concat(RequestConfig.Instance?.ReadConfiguration(c => c.HomeRows.Select(r => CustomRowPrefix + r.Id).ToList()) ?? new List<string>());
        var posters = new HashSet<(string Size, string File)>();
        foreach (var key in keys)
        {
            if (ListFor(key) is not { } list)
            {
                continue;
            }

            for (var number = 1; number <= PreviewPages; number++)
            {
                if (await _tmdb.PageAsync(list.Type, list.Path, list.Query, number, cancellationToken, list.Row).ConfigureAwait(false) is not { } page)
                {
                    break;
                }

                // TMDb's address ends in the size and the file, which is what the page asks the server for.
                foreach (var url in page.Items.Select(i => i.PosterUrl).OfType<string>())
                {
                    var parts = url.Split('/');
                    if (parts.Length >= 2)
                    {
                        posters.Add((parts[^2], parts[^1]));
                    }
                }

                if (number >= page.TotalPages)
                {
                    break;
                }
            }
        }

        var kept = 0;
        foreach (var some in posters.Chunk(PostersAtOnce))
        {
            var paths = await Task.WhenAll(some.Select(p => _artwork.GetAsync(p.Size, p.File, cancellationToken))).ConfigureAwait(false);
            kept += paths.Count(path => path is not null);
        }

        // Only once the rows were read: with none to keep (TMDb's rows couldn't be read), everything stays.
        var gone = clearOthers && posters.Count > 0 ? _artwork.Clear(posters.Select(p => p.Size + "/" + p.File).ToHashSet(StringComparer.Ordinal)) : 0;
        _logger.LogDebug("Kept {Kept} of the request page's {Posters} row posters; let go of {Gone} other images", kept, posters.Count, gone);
        return kept;
    }

    /// <summary>
    /// The TMDb list behind a row, by its key, as it's read here: one picked by streaming services is asked of the
    /// server's country. Null for a key there's no row for.
    /// </summary>
    /// <param name="key">The row's key.</param>
    /// <returns>The list.</returns>
    internal TmdbList? ListFor(string key)
        => TmdbList(key) is { } list && list.Query.ContainsKey("with_watch_providers")
            ? list with { Query = new Dictionary<string, string>(list.Query, StringComparer.Ordinal) { ["watch_region"] = _tmdb.Locale().Region ?? "US" } }
            : TmdbList(key);

    /// <summary>The TMDb list behind a row, by its key; null for a key there's no row for.</summary>
    /// <param name="key">The row's key.</param>
    /// <returns>The list.</returns>
    internal static TmdbList? TmdbList(string key)
    {
        var today = DateTime.UtcNow.Date;
        var from = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = (today + UpcomingWindow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return key switch
        {
            "TrendingMovies" => new TmdbList(RequestType.Movie, "trending/movie/week", new Dictionary<string, string>(), key),
            "TrendingShows" => new TmdbList(RequestType.Series, "trending/tv/week", new Dictionary<string, string>(), key),
            "UpcomingMovies" => new TmdbList(RequestType.Movie, "discover/movie", new Dictionary<string, string>
            {
                ["sort_by"] = "popularity.desc", ["primary_release_date.gte"] = from, ["primary_release_date.lte"] = to, ["with_release_type"] = "2|3"
            }, key),
            "UpcomingShows" => new TmdbList(RequestType.Series, "discover/tv", new Dictionary<string, string>
            {
                ["sort_by"] = "popularity.desc", ["first_air_date.gte"] = from, ["first_air_date.lte"] = to
            }, key),
            "PopularMovies" => new TmdbList(RequestType.Movie, "discover/movie", new Dictionary<string, string> { ["sort_by"] = "popularity.desc" }, key),
            "PopularShows" => new TmdbList(RequestType.Series, "discover/tv", new Dictionary<string, string> { ["sort_by"] = "popularity.desc" }, key),
            _ when key.StartsWith(CustomRowPrefix, StringComparison.Ordinal) => CustomList(key),
            _ => null
        };
    }

    // A home row of the server's own: its type's titles with any of its genres, keywords, studios, networks or streaming
    // services, in its order.
    private static TmdbList? CustomList(string key)
    {
        var id = key[CustomRowPrefix.Length..];
        if (RequestConfig.Instance?.ReadConfiguration(c => c.HomeRows)?.FirstOrDefault(r => r.Id == id) is not { } row)
        {
            return null;
        }

        var param = row.Kind switch
        {
            "Genre" => "with_genres",
            "Keyword" => "with_keywords",
            "Company" => "with_companies",
            "Network" when row.Type == RequestType.Series => "with_networks",
            "Provider" => "with_watch_providers",
            _ => null
        };
        if (param is null || row.Ids.Length == 0)
        {
            return null;
        }

        var order = new TitleFilter { Sort = TitleFilter.Sorts.FirstOrDefault(s => s == row.Sort) };
        var query = new Dictionary<string, string>(order.DiscoverQuery(row.Type, "US"), StringComparer.Ordinal) { [param] = row.Ids.Replace(',', '|') };
        query.TryAdd("sort_by", "popularity.desc");
        // Kept by the daily refresh as the built-in rows are, but under the list itself rather than the row: a row
        // changed in the settings is another list, so it shows as changed straight away.
        return new TmdbList(row.Type, row.Type == RequestType.Movie ? "discover/movie" : "discover/tv", query);
    }

    /// <summary>The TMDb list of a type's titles with a genre, from a studio, or on a network, most popular first.</summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="kind">Genre, Company or Network (shows only).</param>
    /// <param name="id">Its TMDb id.</param>
    /// <returns>The list, or null for a kind there's no such list of.</returns>
    internal static TmdbList? BrowseList(RequestType type, string kind, int id)
    {
        var filter = kind switch
        {
            BrowseLink.Genre => "with_genres",
            BrowseLink.Company => "with_companies",
            BrowseLink.Network when type == RequestType.Series => "with_networks",
            _ => null
        };
        return filter is null || id <= 0 ? null
            : new TmdbList(type, type == RequestType.Movie ? "discover/movie" : "discover/tv", new Dictionary<string, string>
            {
                ["sort_by"] = "popularity.desc", [filter] = id.ToString(CultureInfo.InvariantCulture)
            });
    }

    /// <summary>
    /// Reads a TMDb list from a place in it, marked for the viewer as search results are; titles the blocklist holds
    /// back from them, and those the filter leaves out, are skipped. Starts and Next count in the list's own order, so a
    /// page that skips titles still carries on where it stopped. A read looks through at most a few of TMDb's pages, so a
    /// filter few titles pass returns what it found so far, and Next to carry on from.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="startIndex">Where to start in the list.</param>
    /// <param name="limit">The most titles to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="filter">What the titles are narrowed to, if anything.</param>
    /// <param name="maxPages">The most of TMDb's pages to look through.</param>
    /// <param name="requests">The open requests, when the caller already has them.</param>
    /// <returns>The titles, how many the list holds, and where to carry on (null at its end); null when TMDb didn't answer.</returns>
    public async Task<ListPage?> ListAsync(TmdbList list, UserAccess viewer, int startIndex, int limit, CancellationToken cancellationToken, TitleFilter? filter = null, int maxPages = MaxPagesPerRead, IReadOnlyList<MediaRequest>? requests = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(viewer);
        // A list TMDb discovers is sorted and narrowed by TMDb itself, by what of the filter it can; read so, it isn't
        // the stored copy of a home row.
        if (filter is { AsksTmdb: true } && list.Path.StartsWith("discover/", StringComparison.Ordinal))
        {
            var query = new Dictionary<string, string>(list.Query, StringComparer.Ordinal);
            foreach (var (name, value) in filter.DiscoverQuery(list.Type, _tmdb.Locale().Region ?? "US"))
            {
                query[name] = query.TryGetValue(name, out var own) ? Narrower(name, own, value) : value;
            }

            list = list with { Query = query, Row = null };
        }

        Used();
        var config = RequestConfig.Instance?.ReadConfiguration(c => c);
        requests ??= _db.OpenRequests();
        var library = new Dictionary<Guid, BaseItem>();
        var reasons = new Dictionary<SearchResult, string?>(ReferenceEqualityComparer.Instance);
        // One wait for shows' seasons for the whole read, however many of TMDb's pages it looks through.
        var seasons = new WaitBudget();
        var items = new List<SearchResult>();
        int? next = Math.Max(0, startIndex);
        var total = 0;
        var pagesRead = 0;
        // A filter on where titles stand skips titles only known once each is looked up, so the next of TMDb's pages is
        // fetched while this one is: the reads that skip the most don't wait on TMDb as well.
        var readAhead = filter is { } f && (f.InLibrary is not null || f.StatesWanted.Count > 0);
        Task<TmdbPage?>? ahead = null;
        var aheadNumber = 0;
        while (next is { } at && items.Count < limit && pagesRead++ < maxPages)
        {
            var number = (at / TmdbDiscovery.PageSize) + 1;
            var page = await (ahead is not null && aheadNumber == number ? ahead : _tmdb.PageAsync(list.Type, list.Path, list.Query, number, cancellationToken, list.Row)).ConfigureAwait(false);
            ahead = null;
            if (readAhead && page is not null && number < page.TotalPages && pagesRead < maxPages && items.Count < limit)
            {
                aheadNumber = number + 1;
                ahead = _tmdb.PageAsync(list.Type, list.Path, list.Query, aheadNumber, cancellationToken, list.Row);
                // Left unread when this page is enough; a failure then is nobody's to see.
                _ = ahead.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }

            if (page is null)
            {
                if (items.Count == 0)
                {
                    return null;
                }

                break;
            }

            total = Math.Min(page.TotalResults, page.TotalPages * TmdbDiscovery.PageSize);
            var offset = at % TmdbDiscovery.PageSize;

            // Titles held back, or of a type or genre the filter leaves out, are dropped first; the rest are marked (library,
            // Radarr or Sonarr, requests), as the rest of the filter reads what they say. The library is looked through
            // while Radarr or Sonarr are asked.
            var candidates = page.Items.Skip(offset).ToList();
            var worth = candidates.Where(r => !Hidden(r, viewer, config, reasons) && (filter is null || filter.MatchesTitle(r))).ToList();
            var held = HeldAsync(list.Type, worth, requests, cancellationToken);
            foreach (var result in worth)
            {
                MarkLibrary(result, viewer, library);
            }

            var owned = await held.ConfigureAwait(false);
            foreach (var result in worth)
            {
                MarkArr(result, owned);
            }

            await FillSeasonCountsAsync(worth, seasons, cancellationToken).ConfigureAwait(false);
            Annotate(worth, requests, viewer, library, config, reasons);
            var kept = worth.ToHashSet();
            var before = Earlier(list, number);
            var taken = offset;
            foreach (var result in candidates)
            {
                taken++;
                if (kept.Contains(result) && (filter is null || filter.Matches(result, viewer.IsAdministrator)) && !items.Any(r => SameTitle(r, result))
                    && !(result.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) && before.Contains(tmdb)))
                {
                    items.Add(result);
                    if (items.Count >= limit)
                    {
                        break;
                    }
                }
            }

            // Stopped part way through the page, the next read starts after the last title looked at; through it, at the
            // next page, unless that was the list's last.
            var pageStart = at / TmdbDiscovery.PageSize * TmdbDiscovery.PageSize;
            var resume = taken < page.Items.Count ? pageStart + taken : pageStart + TmdbDiscovery.PageSize;
            next = resume >= total ? null : resume;
        }

        return new ListPage(items, total, next);
    }

    // MARK: ViewerListAsync

    /// <summary>
    /// Reads one of the rows made for the viewer from a place in it, marked as search results are: Recently Added, the
    /// library's newest movies and shows the viewer may see; or Recommended for You, TMDb's recommendations for what the
    /// viewer watched last (their favourites when they've watched little), leaving out what the library has all of.
    /// </summary>
    /// <param name="key">RecentlyAdded or RecommendedForYou.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="startIndex">Where to start in the row.</param>
    /// <param name="limit">The most titles to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="filter">What the titles are narrowed to, if anything.</param>
    /// <param name="requests">The open requests, when the caller already has them.</param>
    /// <returns>The titles, how many the row holds, and where to carry on (null at its end); null for another key, or Recommended for You while TMDb can't be asked.</returns>
    public async Task<ListPage?> ViewerListAsync(string key, UserAccess viewer, int startIndex, int limit, CancellationToken cancellationToken, TitleFilter? filter = null, IReadOnlyList<MediaRequest>? requests = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (!IsViewerRow(key) || (key == RecommendedRow && !_tmdb.Available))
        {
            return null;
        }

        Used();
        var types = new List<RequestType>();
        if (viewer.Movies)
        {
            types.Add(RequestType.Movie);
        }

        if (viewer.Series)
        {
            types.Add(RequestType.Series);
        }

        if (filter is { Types.Count: > 0 })
        {
            types.RemoveAll(t => !filter.Types.Contains(t));
        }

        var all = viewer.User is not { } user || types.Count == 0 ? new List<SearchResult>()
            : key == RecentlyAddedRow ? RecentlyAdded(types, user, viewer, requests ?? _db.OpenRequests())
            : await RecommendedAsync(types, user, viewer, cancellationToken).ConfigureAwait(false);
        var matched = filter is null ? all : all.Where(r => filter.Matches(r, viewer.IsAdministrator)).ToList();
        var start = Math.Max(0, startIndex);
        var page = matched.Skip(start).Take(Math.Max(0, limit)).ToList();
        return new ListPage(page, matched.Count, start + page.Count < matched.Count ? start + page.Count : null);
    }

    // The library's newest movies and shows the viewer may see, marked as search results are.
    private List<SearchResult> RecentlyAdded(IReadOnlyCollection<RequestType> types, User user, UserAccess viewer, IReadOnlyList<MediaRequest> requests)
    {
        var found = _library.RecentlyAdded(types, ViewerRowSize, user).Where(i => LibraryMatcher.KindOf(i).HasValue).ToList();
        var items = found.ToDictionary(i => i.Id);
        var results = found.Select(i => FromLibrary(i, LibraryMatcher.KindOf(i)!.Value)).ToList();
        Annotate(results, requests, viewer, items);
        return results;
    }

    // TMDb's recommendations for the viewer's latest watched titles, those more of them recommend first, then in the
    // order TMDb gives them; what the library has all of, and the watched titles themselves, left out.
    private async Task<List<SearchResult>> RecommendedAsync(IReadOnlyCollection<RequestType> types, User user, UserAccess viewer, CancellationToken cancellationToken)
    {
        var seeds = _library.RecentlyWatched(types, RecommendationSeeds, user);
        if (seeds.Count == 0)
        {
            return new List<SearchResult>();
        }

        var watched = seeds.Select(s => (Type: LibraryMatcher.KindOf(s)!.Value, Ids: ProviderIds.Clean(s.ProviderIds))).ToList();
        var lists = await Task.WhenAll(watched.Select(seed => RecommendationsFor(seed.Type, seed.Ids, cancellationToken))).ConfigureAwait(false);
        var ranked = Ranked(lists, watched.Select(w => w.Ids.TryGetValue(ProviderIds.Tmdb, out var id) ? w.Type + "|" + id : null).OfType<string>().ToHashSet(StringComparer.Ordinal), types);
        var marked = await MarkTitlesAsync(ranked.Take(ViewerRowSize * 2).ToList(), viewer, cancellationToken).ConfigureAwait(false);
        return marked.Where(r => r.LibraryItemId is null || r.Partial).Take(ViewerRowSize).ToList();
    }

    // One watched title's recommendations from TMDb; none when TMDb doesn't know it or can't be asked.
    private async Task<IReadOnlyList<SearchResult>> RecommendationsFor(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        try
        {
            var tmdbId = await _tmdb.TmdbIdAsync(type, ids, cancellationToken).ConfigureAwait(false);
            return tmdbId is not null && await _tmdb.RecommendationsAsync(type, tmdbId, cancellationToken).ConfigureAwait(false) is { } page
                ? page.Items
                : Array.Empty<SearchResult>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Reading TMDb's recommendations for a watched title failed");
            return Array.Empty<SearchResult>();
        }
    }

    /// <summary>
    /// Puts several titles' recommendations in one order: those more of the lists hold first, then by the best place any
    /// list gives them, then by which list came first. Each title once, by its type and TMDb id; titles without one,
    /// of types not wanted, and the excluded ones (the watched titles themselves) left out.
    /// </summary>
    /// <param name="lists">Each title's recommendations, in the order its title was watched.</param>
    /// <param name="excluded">Type|TMDb id keys to leave out.</param>
    /// <param name="types">The types wanted.</param>
    /// <returns>The titles, in order.</returns>
    internal static List<SearchResult> Ranked(IReadOnlyList<IReadOnlyList<SearchResult>> lists, IReadOnlySet<string> excluded, IReadOnlyCollection<RequestType> types)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(excluded);
        ArgumentNullException.ThrowIfNull(types);
        var found = new Dictionary<string, (SearchResult Title, int Count, int Best, int First)>(StringComparer.Ordinal);
        for (var list = 0; list < lists.Count; list++)
        {
            for (var place = 0; place < lists[list].Count; place++)
            {
                var title = lists[list][place];
                if (!types.Contains(title.Type) || !title.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var id) || string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var key = title.Type + "|" + id;
                if (excluded.Contains(key))
                {
                    continue;
                }

                found[key] = found.TryGetValue(key, out var seen)
                    ? (seen.Title, seen.Count + 1, Math.Min(seen.Best, place), seen.First)
                    : (title, 1, place, list);
            }
        }

        return found.Values.OrderByDescending(f => f.Count).ThenBy(f => f.Best).ThenBy(f => f.First).Select(f => f.Title).ToList();
    }

    /// <summary>Gets TMDb's recommendations for a title, marked for the viewer as search results are.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="filter">What the titles are narrowed to, if anything.</param>
    /// <returns>The titles; empty when there are none or TMDb can't be asked.</returns>
    public async Task<List<SearchResult>> SimilarAsync(RequestType type, IReadOnlyDictionary<string, string> ids, UserAccess viewer, CancellationToken cancellationToken, TitleFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(viewer);
        Used();
        var tmdbId = await _tmdb.TmdbIdAsync(type, ids, cancellationToken).ConfigureAwait(false);
        if (tmdbId is null || await _tmdb.RecommendationsAsync(type, tmdbId, cancellationToken).ConfigureAwait(false) is not { } page)
        {
            return new List<SearchResult>();
        }

        var items = await MarkAsync(type, page.Items, viewer, cancellationToken).ConfigureAwait(false);
        return filter is null ? items : items.Where(r => filter.Matches(r, viewer.IsAdministrator)).ToList();
    }

    /// <summary>Gets a TMDb collection of movies, such as Dune Collection, its movies marked for the viewer as search results are.</summary>
    /// <param name="id">The collection's TMDb id.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection and its movies; null when TMDb can't be asked or doesn't know it.</returns>
    public async Task<(TmdbMovieSet Set, List<SearchResult> Items)?> CollectionAsync(int id, UserAccess viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        Used();
        if (await _tmdb.CollectionAsync(id, cancellationToken).ConfigureAwait(false) is not { } set)
        {
            return null;
        }

        return (set, await MarkAsync(RequestType.Movie, set.Parts, viewer, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Gets one title by its id, marked as search results are: the library's copy the viewer may see, whether Radarr
    /// or Sonarr has it, and its request.
    /// </summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">Its ids: TMDb, IMDb or, for a show, TVDB.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The title; null when TMDb doesn't know it, can't be asked, or the blocklist holds it back from the viewer.</returns>
    public async Task<SearchResult?> TitleAsync(RequestType type, IReadOnlyDictionary<string, string> ids, UserAccess viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        if (await _tmdb.DescribeAsync(type, ids, cancellationToken).ConfigureAwait(false) is not { } described)
        {
            return null;
        }

        var result = new SearchResult
        {
            Type = type,
            Title = described.Title,
            Year = described.Year,
            Overview = described.Overview,
            PosterUrl = described.PosterUrl,
            BackdropUrl = described.BackdropUrl,
            ProviderIds = new Dictionary<string, string>(described.ProviderIds, StringComparer.OrdinalIgnoreCase),
            Genres = described.Genres,
            Certification = described.Certification,
            Language = described.Traits?.Language,
            SeasonCount = described.ExpectedEpisodes?.Keys.Count(k => k > 0)
        };
        return (await MarkTitlesAsync(new[] { result }, viewer, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    /// <summary>
    /// Marks titles from TMDb of either type as search results are: the library's copy the viewer may see, whether
    /// Radarr or Sonarr has them, and their requests; those the blocklist holds back from the viewer are left out.
    /// </summary>
    /// <param name="titles">The titles, in their order.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The titles kept, in their order.</returns>
    public async Task<List<SearchResult>> MarkTitlesAsync(IReadOnlyList<SearchResult> titles, UserAccess viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(viewer);
        Used();
        var kept = (await Task.WhenAll(titles.GroupBy(t => t.Type).Select(g => MarkAsync(g.Key, g, viewer, cancellationToken))).ConfigureAwait(false))
            .SelectMany(g => g).ToHashSet();
        return titles.Where(kept.Contains).ToList();
    }

    // Titles from TMDb marked as search marks them: the library's copy the viewer may see, whether Radarr or Sonarr has
    // them, and their requests; those the blocklist holds back from the viewer left out.
    private async Task<List<SearchResult>> MarkAsync(RequestType type, IEnumerable<SearchResult> titles, UserAccess viewer, CancellationToken cancellationToken)
    {
        var config = RequestConfig.Instance?.ReadConfiguration(c => c);
        var requests = _db.OpenRequests();
        var library = new Dictionary<Guid, BaseItem>();
        var reasons = new Dictionary<SearchResult, string?>(ReferenceEqualityComparer.Instance);
        var items = titles.Where(r => !Hidden(r, viewer, config, reasons)).ToList();
        var held = HeldAsync(type, items, requests, cancellationToken);
        foreach (var result in items)
        {
            MarkLibrary(result, viewer, library);
        }

        var owned = await held.ConfigureAwait(false);
        foreach (var result in items)
        {
            MarkArr(result, owned);
        }

        Annotate(items, requests, viewer, library, config, reasons);
        return items;
    }

    /// <summary>
    /// Gets a season of a show and its episodes from TMDb, each marked with whether the library has it for the viewer.
    /// </summary>
    /// <param name="lookup">The show and the season's number.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The season; null when TMDb can't be asked or doesn't know it.</returns>
    public async Task<SeasonEpisodes?> EpisodesAsync(EpisodesLookup lookup, UserAccess viewer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(viewer);
        var ids = ProviderIds.Clean(lookup.ProviderIds);
        if (await _tmdb.EpisodesAsync(ids, lookup.Season, cancellationToken).ConfigureAwait(false) is not { } season)
        {
            return null;
        }

        // Only the copy the viewer may see counts, as everywhere.
        if (viewer.User is { } user && _library.Find(RequestType.Series, ids, lookup.Title, lookup.Year, user) is { } series)
        {
            var have = _library.EpisodeNumbers(series, lookup.Season);
            season.Episodes.ForEach(e => e.InLibrary = have.Contains(e.Number));
        }

        return season;
    }

    /// <summary>Gets the streaming services TMDb knows in the server's country for a type, most prominent first.</summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The services.</returns>
    public Task<List<TmdbChoice>> ProvidersAsync(RequestType type, CancellationToken cancellationToken)
        => _tmdb.ProvidersAsync(type, cancellationToken);

    /// <summary>Finds what a home row can pick its titles by (see TmdbDiscovery.ChoicesAsync).</summary>
    /// <param name="kind">Genre, Keyword, Company or Provider.</param>
    /// <param name="type">Movies or shows.</param>
    /// <param name="query">For keywords and studios, the name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The choices.</returns>
    public Task<List<TmdbChoice>> ChoicesAsync(string kind, RequestType type, string? query, CancellationToken cancellationToken)
        => _tmdb.ChoicesAsync(kind, type, query, cancellationToken);

    /// <summary>Gets every genre TMDb has for some types, by name, for filtering by.</summary>
    /// <param name="types">The types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The names, sorted; empty without TMDb.</returns>
    public Task<List<string>> GenreNamesAsync(IEnumerable<RequestType> types, CancellationToken cancellationToken)
        => _tmdb.GenreNamesAsync(types, cancellationToken);

    /// <summary>Gets what a title can be browsed by on TMDb: its genres, studios and networks.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links; empty when TMDb can't be asked.</returns>
    public async Task<List<BrowseLink>> BrowseLinksAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _tmdb.Available && await _tmdb.TmdbIdAsync(type, ids, cancellationToken).ConfigureAwait(false) is { } tmdbId
            ? await _tmdb.LinksAsync(type, tmdbId, cancellationToken).ConfigureAwait(false)
            : new List<BrowseLink>();
    }

    // How long a list or search waits on an extra answer (a show's seasons, whether a movie's file has come) before
    // going on without it. Each is still read to the end and kept, so the next look has it.
    private static readonly TimeSpan QuickWait = TimeSpan.FromMilliseconds(300);

    // How many seasons each show the viewer's library has (already matched: LibraryItemId) has, from TMDb (kept until the next refresh),
    // where its search didn't say. Waited on for what's left of the budget, which a read of many pages shares.
    private async Task FillSeasonCountsAsync(IEnumerable<SearchResult> results, WaitBudget budget, CancellationToken cancellationToken)
    {
        var counting = results.Where(r => r.Type == RequestType.Series && r.SeasonCount is null && r.LibraryItemId is not null)
            .Select(r => (Result: r, Count: _tmdb.SeasonCountAsync(r.ProviderIds, CancellationToken.None)))
            .ToList();
        if (counting.Count == 0)
        {
            return;
        }

        var all = Task.WhenAll(counting.Select(c => c.Count));
        if (!all.IsCompleted)
        {
            _ = all.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            var left = budget.Left();
            if (left > TimeSpan.Zero)
            {
                await Task.WhenAny(all, Task.Delay(left, cancellationToken)).ConfigureAwait(false);
            }
        }

        foreach (var (result, count) in counting.Where(c => c.Count.IsCompletedSuccessfully))
        {
            result.SeasonCount = await count.ConfigureAwait(false);
        }
    }

    // How long is left of one wait shared by several: QuickWait from the first time it's asked.
    private sealed class WaitBudget
    {
        private DateTime? _until;

        public TimeSpan Left()
        {
            _until ??= DateTime.UtcNow + QuickWait;
            return _until.Value - DateTime.UtcNow;
        }
    }

    // A list's own TMDb parameter and the filter's, together: a filter only ever narrows a list (Upcoming's window, a
    // row's own streaming services), never widens or replaces it. The later of two from-dates, the earlier of two
    // to-dates, the larger of two least counts or ratings, and the services both name; the filter's order wins.
    internal static string Narrower(string name, string own, string filter)
    {
        if (name.EndsWith(".gte", StringComparison.Ordinal))
        {
            return Compare(own, filter) >= 0 ? own : filter;
        }

        if (name.EndsWith(".lte", StringComparison.Ordinal))
        {
            return Compare(own, filter) <= 0 ? own : filter;
        }

        if (name is "with_watch_providers" or "with_original_language")
        {
            var both = own.Split('|').Intersect(filter.Split('|'), StringComparer.Ordinal).ToList();
            return both.Count > 0 ? string.Join('|', both) : "0";
        }

        return filter;
    }

    // Dates (yyyy-MM-dd) compare as text; numbers as numbers.
    private static int Compare(string a, string b)
        => double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? x.CompareTo(y)
            : string.CompareOrdinal(a, b);

    // How far back a list's page is checked for titles already listed.
    private const int EarlierPages = 5;

    // The TMDb ids on the few pages of a list before this one, as they were read. A list changes as it's read (and a
    // home row's first pages are a stored copy), so a title can move to a later page: one already listed earlier isn't
    // listed again. Pages not read lately aren't fetched just for this.
    private HashSet<string> Earlier(TmdbList list, int number)
        => Enumerable.Range(Math.Max(1, number - EarlierPages), Math.Min(number - 1, EarlierPages))
            .SelectMany(n => _tmdb.KnownIds(list.Path, list.Query, n, list.Row))
            .ToHashSet(StringComparer.Ordinal);

    // Which of some titles Radarr or Sonarr has, by the id each knows a title by: TMDb for movies, TVDB for shows, else a
    // show's TMDb id, which a Sonarr whose library was read lately knows its shows by too. Where that isn't enough (a
    // Sonarr not read lately, or one that doesn't list TMDb ids), or an open request knows a show only by its TVDB id,
    // the shows' TVDB ids are looked up first, for a moment only.
    private async Task<ArrHeld> HeldAsync(RequestType type, IReadOnlyCollection<SearchResult> titles, IReadOnlyList<MediaRequest> requests, CancellationToken cancellationToken)
    {
        if (type == RequestType.Series && (!_arr.KnowsByTmdb(type) || IndexOf(requests).ShowsByTvdbOnly))
        {
            await FillTvdbAsync(titles, cancellationToken).ConfigureAwait(false);
        }

        return await _arr.HeldAsync(type, IdsOf(type, titles), cancellationToken).ConfigureAwait(false);
    }

    // Adds shows' TVDB ids where they're known or TMDb gives them within QuickWait. Each lookup carries on to the end and
    // is kept, so the next look has it.
    private async Task FillTvdbAsync(IEnumerable<SearchResult> shows, CancellationToken cancellationToken)
    {
        var lookups = shows.Where(s => s.Type == RequestType.Series && !s.ProviderIds.ContainsKey(ProviderIds.Tvdb) && s.ProviderIds.ContainsKey(ProviderIds.Tmdb))
            .Select(s => (Show: s, Id: _tmdb.TvdbIdAsync(s.ProviderIds[ProviderIds.Tmdb], CancellationToken.None)))
            .ToList();
        var all = Task.WhenAll(lookups.Select(l => l.Id));
        if (!all.IsCompleted)
        {
            _ = all.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await Task.WhenAny(all, Task.Delay(QuickWait, cancellationToken)).ConfigureAwait(false);
        }

        foreach (var (show, id) in lookups.Where(l => l.Id.IsCompletedSuccessfully))
        {
            // Already done, so this doesn't wait.
            if (await id.ConfigureAwait(false) is { } tvdb)
            {
                show.ProviderIds[ProviderIds.Tvdb] = tvdb;
            }
        }
    }

    private static IEnumerable<string> IdsOf(RequestType type, IEnumerable<SearchResult> titles)
        => titles.Select(t => HeldId(type, t)).OfType<string>();

    // The id Radarr or Sonarr keeps a title by: TMDb for movies, TVDB for shows, else a show's TMDb id.
    private static string? HeldId(RequestType type, SearchResult title)
        => title.ProviderIds.TryGetValue(type == RequestType.Movie ? ProviderIds.Tmdb : ProviderIds.Tvdb, out var id) ? id
            : type == RequestType.Series && title.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) ? ArrClient.TmdbKey(tmdb)
            : null;

    // A title from a list outside the library and Radarr or Sonarr, marked as search marks one: the library's copy when
    // the viewer may see it (MarkLibrary), and whether Radarr or Sonarr already has it and its files (MarkArr).
    private void MarkLibrary(SearchResult result, UserAccess viewer, Dictionary<Guid, BaseItem> library)
    {
        var item = viewer.User is null ? null : _library.Find(result.Type, result.ProviderIds, result.Title, result.Year, viewer.User);
        result.LibraryItemId = item?.Id;
        if (item is not null)
        {
            library.TryAdd(item.Id, item);
        }
    }

    private static void MarkArr(SearchResult result, ArrHeld owned)
    {
        if (HeldId(result.Type, result) is { } id && owned.Ids.Contains(id))
        {
            result.InArr = true;
            result.ArrHasFiles = owned.WithFiles.Contains(id);
        }
    }

    /// <summary>Returns whether a title is kept from the viewer: a blocked one, for anyone but administrators.</summary>
    /// <param name="result">The title.</param>
    /// <param name="viewer">Who is looking.</param>
    /// <param name="config">The configuration, for the blocklist.</param>
    /// <returns>True when it's left out.</returns>
    internal static bool HiddenFrom(SearchResult result, UserAccess viewer, RequestSettings? config)
        => !viewer.IsAdministrator && config is not null && BlockReason(result, config) is not null;

    // Whether a title is kept from the viewer (HiddenFrom), noting why it's blocked, if it is, for Annotate to mark it
    // with: each title is checked against the blocklist once.
    private static bool Hidden(SearchResult result, UserAccess viewer, RequestSettings? config, Dictionary<SearchResult, string?> reasons)
    {
        if (config is null)
        {
            return false;
        }

        var reason = BlockReason(result, config);
        reasons[result] = reason;
        return !viewer.IsAdministrator && reason is not null;
    }

    // Why a list's title is blocked, or null.
    private static string? BlockReason(SearchResult result, RequestSettings config)
        => Blocklist.Reason(config, result.Type, result.ProviderIds, result.Title, result.Genres, result.Certification, ListTraits(result));

    // What a list's title says of the blocklist's TMDb traits: its language. Its keywords and collection are only on its
    // own page, so a title blocked by those is refused when it's requested.
    private static TitleTraits ListTraits(SearchResult result) => new(Array.Empty<int>(), result.Language, null);

    // Whether two titles are the same: the same library item, or any provider id in common.
    private static bool SameTitle(SearchResult a, SearchResult b)
        => a.Type == b.Type && ((a.LibraryItemId is { } id && id == b.LibraryItemId) || ProviderIds.Overlap(a.ProviderIds, b.ProviderIds));

    // Whether a request is pointed out to a viewer: not someone else's for a title partly in a library hidden from them,
    // whose state would say what that library holds.
    private bool Shows(MediaRequest request, UserAccess? viewer)
        => viewer is null || viewer.IsAdministrator || request.UserId == viewer.UserId
            || request.Joined?.Any(j => j.UserId == viewer.UserId) == true
            || request.LibraryItemId is not { } id || _library.Get(id) is not { } item
            || (viewer.User is not null && LibraryMatcher.IsVisibleTo(item, viewer.User));

    // Marks titles with what the requests, the blocklist and Radarr already hold for the viewer: a request's standing
    // and who made it, whether a show is only partly there, whether the title is blocked (why, for administrators),
    // and whether a movie is downloaded. items are the library items the titles were matched to, by id; reasons are
    // the titles' blocklist reasons, when the caller already checked them (see Hidden), and config the configuration
    // they were checked against.
    private void Annotate(List<SearchResult> results, IReadOnlyList<MediaRequest> requests, UserAccess? viewer, Dictionary<Guid, BaseItem> items, RequestSettings? config = null, Dictionary<SearchResult, string?>? reasons = null)
    {
        var byId = IndexOf(requests).ById;
        foreach (var result in results)
        {
            // A show that is only partly requested or partly in the library can still be requested. A request Radarr or
            // Sonarr has dropped doesn't stop anyone asking again, so it's shown only to its requester, who may remove it,
            // and only when no other request for the title holds a place.
            var request = result.ProviderIds
                .Where(i => !string.IsNullOrEmpty(i.Value))
                .SelectMany(i => byId[IdKey(result.Type, i.Key, i.Value)])
                .OrderBy(x => x.Order)
                .Select(x => x.Request)
                .Distinct()
                .Where(r => ProviderIds.Matches(r, result.Type, result.ProviderIds) && (r.HoldsAPlace || (viewer is not null && r.UserId == viewer.UserId)) && Shows(r, viewer))
                .OrderBy(r => r.HoldsAPlace ? 0 : 1)
                .FirstOrDefault();
            result.RequestStatus = request?.Status;
            result.RequestId = request?.Id;
            result.RequestedBy = request is null || viewer is null ? null
                : request.UserId == viewer.UserId || viewer.IsAdministrator || viewer.SeeRequesters ? request.UserName
                : null;
            result.CanCancel = request is not null && viewer is not null && CanCancel(request, viewer);
            result.Joined = request is not null && viewer is not null && request.Joined?.Any(j => j.UserId == viewer.UserId) == true;
            result.Partial = result.Type == RequestType.Series
                && (request?.Seasons is not null || (result.LibraryItemId is { } id && items.TryGetValue(id, out var series) && MissingSeasons(series, result.SeasonCount)));
        }

        // Blocked titles are marked, with the reason for administrators, who may still request them.
        config ??= RequestConfig.Instance?.ReadConfiguration(c => c);
        if (config is not null)
        {
            foreach (var result in results)
            {
                var reason = reasons is not null && reasons.TryGetValue(result, out var known) ? known : BlockReason(result, config);
                result.Blocked = reason is not null;
                result.BlockedReason = viewer?.IsAdministrator == true ? reason : null;
            }
        }

        // A downloaded movie is on its way to the library, which everyone may know, unless it's already in a library
        // the viewer can't see: then it shows as any other title the server has without files, so nothing on the card
        // says where it is, and a request for it is refused as usual.
        foreach (var result in results.Where(r => r.Type == RequestType.Movie && r.ArrHasFiles))
        {
            result.Downloaded = viewer is not { IsAdministrator: false } || result.LibraryItemId is not null
                || _library.Find(result.Type, result.ProviderIds, result.Title, result.Year) is null;
        }
    }

    private static string IdKey(RequestType type, string provider, string id)
        => type + "|" + provider.ToUpperInvariant() + "|" + id.ToUpperInvariant();

    /// <summary>Gets the open requests by each of their ids, built once for each list of them.</summary>
    /// <param name="requests">The open requests, newest first.</param>
    /// <returns>The index.</returns>
    internal static RequestIndex IndexOf(IReadOnlyList<MediaRequest> requests) => _indexes.GetValue(requests, r => new RequestIndex(r));

    /// <summary>
    /// The open requests by each of their ids, rather than every request looked through for every title; each id's in
    /// the order they're listed, newest first.
    /// </summary>
    internal sealed class RequestIndex
    {
        /// <summary>Initializes a new instance of the <see cref="RequestIndex"/> class.</summary>
        /// <param name="requests">The open requests, newest first.</param>
        public RequestIndex(IReadOnlyList<MediaRequest> requests)
        {
            ById = requests
                .SelectMany((r, order) => r.ProviderIds.Where(i => !string.IsNullOrEmpty(i.Value)).Select(i => (Key: IdKey(r.Type, i.Key, i.Value), Request: r, Order: order)))
                .ToLookup(x => x.Key, x => (x.Request, x.Order));
            ShowsByTvdbOnly = requests.Any(r => r.Type == RequestType.Series
                && r.ProviderIds.TryGetValue(ProviderIds.Tvdb, out var tvdb) && !string.IsNullOrEmpty(tvdb)
                && !(r.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) && !string.IsNullOrEmpty(tmdb)));
        }

        /// <summary>Gets the requests by each id, keyed by type, provider and id.</summary>
        public ILookup<string, (MediaRequest Request, int Order)> ById { get; }

        /// <summary>Gets a value indicating whether any open request is for a show known by its TVDB id and not its TMDb id.</summary>
        public bool ShowsByTvdbOnly { get; }
    }

    // Whether a search result offers to cancel its request: administrators one waiting for a decision or a retry;
    // requesters their own while it waits for a decision, or once Radarr or Sonarr has dropped it, which removing
    // then allows (see RequestService.MayRemove).
    internal static bool CanCancel(MediaRequest request, UserAccess viewer)
        => viewer.IsAdministrator
            ? request.Status is RequestStatus.Pending or RequestStatus.Failed || (request.IsOpen && request.RemovedFromArr)
            : request.UserId == viewer.UserId && (request.Status == RequestStatus.Pending || (request.IsOpen && request.RemovedFromArr));

    // Whether a library show lacks seasons: fewer complete ones than TMDb says it has, or any
    // season the library holds only part of.
    private bool MissingSeasons(BaseItem series, int? seasonCount)
    {
        var counts = _library.CountEpisodes(series);
        var complete = LibraryMatcher.CompleteSeasons(counts, null);
        var held = counts.Have.Keys.Where(s => s > 0).ToList();
        return held.Any(s => !complete.Contains(s)) || (seasonCount is { } total && complete.Count < total);
    }

    // A title from the library, described from Jellyfin's own record of it.
    internal static SearchResult FromLibrary(BaseItem item, RequestType type)
    {
        var original = item.OriginalTitle;
        var trailer = item.RemoteTrailers?.Select(t => t.Url).FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && (uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "youtu.be"));
        return new SearchResult
        {
            Type = type,
            Title = item.Name,
            Year = item.ProductionYear ?? item.PremiereDate?.Year,
            Overview = item.Overview,
            PosterUrl = RequestService.LibraryPosterUrl(item),
            BackdropUrl = RequestService.LibraryBackdropUrl(item),
            LogoUrl = RequestService.LibraryLogoUrl(item),
            ProviderIds = ProviderIds.Clean(item.ProviderIds),
            LibraryItemId = item.Id,
            OriginalTitle = !string.IsNullOrEmpty(original) && !string.Equals(original, item.Name, StringComparison.OrdinalIgnoreCase) ? original : null,
            Genres = item.Genres?.Take(6).ToList() ?? new List<string>(),
            // Under a minute is a clip or a bad probe, not a running time worth showing.
            RuntimeMinutes = item.RunTimeTicks is { } ticks && (int)Math.Round(TimeSpan.FromTicks(ticks).TotalMinutes) is > 0 and var minutes ? minutes : null,
            Certification = string.IsNullOrEmpty(item.OfficialRating) ? null : item.OfficialRating,
            Studio = item.Studios?.FirstOrDefault(),
            CommunityRating = item.CommunityRating is > 0 and var rating ? Math.Round(rating, 1, MidpointRounding.AwayFromZero) : null,
            RottenTomatoes = item.CriticRating is > 0 and var critics ? (int)Math.Round(critics) : null,
            ReleaseDate = item.PremiereDate,
            Status = item is Series { Status: { } status } ? status.ToString() : null,
            EndYear = item is Series { Status: MediaBrowser.Model.Entities.SeriesStatus.Ended } && item.EndDate is { } end ? end.Year : null,
            TrailerUrl = trailer
        };
    }

    private static IEnumerable<T> Interleave<T>(List<T> a, List<T> b)
    {
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            if (i < a.Count)
            {
                yield return a[i];
            }

            if (i < b.Count)
            {
                yield return b[i];
            }
        }
    }

    // TMDb's search for one type, in one request; nothing when TMDb can't be asked.
    private async Task<List<SearchResult>> SearchKindAsync(RequestType type, string query, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdb.SearchAsync(type, query, cancellationToken).ConfigureAwait(false) ?? new List<SearchResult>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Searching TMDb for {Type} failed", type);
            return new List<SearchResult>();
        }
    }
}
