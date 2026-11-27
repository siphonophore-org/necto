using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SearchResult = Jellyfin.Plugin.Necto.Services.Requests.Models.SearchResult;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// Search finds titles by name or id, builds the home rows from the viewer's library and TMDb's lists, hides what the
/// blocklist refuses, and keeps the rows and their posters ahead of the first visit. The rows read the loaded plugin's
/// settings and Jellyfin's static library, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class SearchServiceTests : IDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), "necto-search-" + Guid.NewGuid().ToString("N"));
    private readonly List<BaseItem> _items = new();
    private readonly ILibraryManager _libraryManager;
    private readonly User _user = new("viewer", "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider", "Jellyfin.Server.Implementations.Users.DefaultPasswordResetProvider");
    private RequestHarness? _harness;
    private List<BaseItem> _newestMovies = new();
    private List<BaseItem> _newestEpisodes = new();

    public SearchServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _libraryManager = InterfaceStub.Of<ILibraryManager>(
            ("GetItemById", a => a[0] is Guid id ? _items.FirstOrDefault(i => i.Id == id) : null),
            ("GetItemList", a => Answer((InternalItemsQuery)a[0]!)));
        BaseItem.LibraryManager = _libraryManager;
    }

    private RequestHarness Harness => _harness ??= new RequestHarness();

    public void Dispose()
    {
        _harness?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, true);
    }

    /// <summary>
    /// A filter narrows a list's own bounds rather than replacing them, and an order the filter picks wins.
    /// True: Upcoming filtered to a later date starts at the later date, and two language choices that cannot meet find nothing.
    /// False: a filter would widen a list past its own bounds.
    /// </summary>
    [Fact]
    public void Narrower_ListAndFilter_TakesTheNarrower()
    {
        Assert.Equal("2026-11-01", SearchService.Narrower("primary_release_date.gte", "2026-11-01", "2020-01-01"));
        Assert.Equal("2026-11-01", SearchService.Narrower("primary_release_date.lte", "2027-05-01", "2026-11-01"));
        Assert.Equal("7", SearchService.Narrower("vote_average.gte", "6.5", "7"));
        Assert.Equal("8|337", SearchService.Narrower("with_watch_providers", "8|337|9", "337|8"));
        Assert.Equal("0", SearchService.Narrower("with_original_language", "en", "ja"));
        Assert.Equal("vote_average.desc", SearchService.Narrower("sort_by", "popularity.desc", "vote_average.desc"));
    }

    /// <summary>
    /// A search for an id finds by that id.
    /// True: typing tmdb:603 finds The Matrix.
    /// False: an id search would look for a title named tmdb:603.
    /// </summary>
    [Theory]
    [InlineData("tmdb:603", "Tmdb", "603")]
    [InlineData(" IMDB: tt0133093 ", "Imdb", "tt0133093")]
    [InlineData("tvdb:81189", "Tvdb", "81189")]
    public void IdSearch_Id_FindsByIt(string query, string key, string value)
    {
        var ids = SearchService.IdSearch(query);

        Assert.NotNull(ids);
        Assert.Equal(value, ids![key]);
    }

    /// <summary>
    /// Any other search is by name, including an id under the wrong provider.
    /// True: a title search is never mistaken for an id.
    /// False: a malformed id would ask TMDb for nothing.
    /// </summary>
    [Theory]
    [InlineData("The Matrix")]
    [InlineData("imdb:603")]
    [InlineData("tmdb:tt0133093")]
    [InlineData("tmdb:")]
    public void IdSearch_Name_IsNull(string query) => Assert.Null(SearchService.IdSearch(query));

    /// <summary>
    /// The empty search shows the viewer's rows, then TMDb's trending, upcoming, and popular rows, each with a TMDb list.
    /// True: the home page lists its rows in this order under these names.
    /// False: a row would be missing, renamed, or have nothing to read.
    /// </summary>
    [Fact]
    public void DiscoverKinds_HomeRows_AreTheViewersThenTmdbs()
    {
        Assert.Equal(new[] { "RecentlyAdded", "RecommendedForYou", "TrendingShows", "TrendingMovies", "UpcomingShows", "UpcomingMovies", "PopularShows", "PopularMovies" }, Array.ConvertAll(SearchService.DiscoverKinds, k => k.Key));
        Assert.Equal(new[] { "Recently Added", "Recommended for you", "Trending shows", "Trending movies", "Upcoming shows", "Upcoming movies", "Popular shows", "Popular movies" }, Array.ConvertAll(SearchService.DiscoverKinds, k => k.Title));
        Assert.All(SearchService.DiscoverKinds.Where(k => !SearchService.IsViewerRow(k.Key)), k => Assert.NotNull(SearchService.TmdbList(k.Key)));
    }

    /// <summary>
    /// The viewer's rows are built in, first, and have no TMDb list.
    /// True: Recently Added and Recommended for you are read from the viewer's library.
    /// False: they would be asked of TMDb, or shown after TMDb's rows.
    /// </summary>
    [Fact]
    public void IsViewerRow_ViewersRows_AreBuiltInAndFirst()
    {
        Assert.Equal(new[] { "RecentlyAdded", "RecommendedForYou" }, SearchService.DiscoverKinds.Take(2).Select(k => k.Key));
        Assert.True(SearchService.IsViewerRow("RecentlyAdded"));
        Assert.True(SearchService.IsViewerRow("RecommendedForYou"));
        Assert.False(SearchService.IsViewerRow("TrendingShows"));
        Assert.False(SearchService.IsViewerRow(null));
        Assert.Null(SearchService.TmdbList("RecentlyAdded"));
        Assert.Null(SearchService.TmdbList("RecommendedForYou"));
    }

    /// <summary>
    /// Blocked titles are left out for requesters but not for administrators.
    /// True: a requester never sees a blocked title or genre, and an administrator can still unblock it.
    /// False: a blocked title would be offered, or hidden from the one who may unblock it.
    /// </summary>
    [Fact]
    public void HiddenFrom_BlockedTitle_HidesForRequestersOnly()
    {
        var config = new RequestSettings
        {
            BlockedTitles = { new BlockedTitle { Type = RequestType.Movie, Title = "Blocked", Tmdb = "2" } },
            BlockedGenres = { "Horror" }
        };
        var requester = new UserAccess { UserId = Guid.NewGuid(), CanRequest = true, Movies = true };
        var admin = new UserAccess { UserId = Guid.NewGuid(), CanRequest = true, Movies = true, IsAdministrator = true };
        SearchResult Title(string name, string tmdb, params string[] genres) => new()
        {
            Type = RequestType.Movie, Title = name, ProviderIds = new Dictionary<string, string> { [ProviderIds.Tmdb] = tmdb }, Genres = new List<string>(genres)
        };

        Assert.True(SearchService.HiddenFrom(Title("Blocked", "2"), requester, config));
        Assert.True(SearchService.HiddenFrom(Title("Scary", "3", "Horror"), requester, config));
        Assert.False(SearchService.HiddenFrom(Title("Fine", "1"), requester, config));
        Assert.False(SearchService.HiddenFrom(Title("Blocked", "2"), admin, config));
    }

    /// <summary>
    /// The TMDb rows and browse lists are TMDb's own lists, and unknown ones are none.
    /// True: each row and browse link reads the list it names.
    /// False: a row would read the wrong list, or an unknown key would ask TMDb for anything.
    /// </summary>
    [Fact]
    public void TmdbList_RowsAndBrowseLinks_AreTmdbsLists()
    {
        Assert.Equal("trending/tv/week", SearchService.TmdbList("TrendingShows")!.Path);
        Assert.Equal("trending/movie/week", SearchService.TmdbList("TrendingMovies")!.Path);
        Assert.Null(SearchService.TmdbList("TopMovies"));
        Assert.Equal("discover/movie", SearchService.TmdbList("PopularMovies")!.Path);
        Assert.Null(SearchService.TmdbList("Custom-nothere"));
        Assert.Equal("2|3", SearchService.TmdbList("UpcomingMovies")!.Query["with_release_type"]);
        Assert.Equal(RequestType.Series, SearchService.TmdbList("UpcomingShows")!.Type);
        Assert.Equal("18", SearchService.BrowseList(RequestType.Movie, BrowseLink.Genre, 18)!.Query["with_genres"]);
        Assert.Equal("discover/tv", SearchService.BrowseList(RequestType.Series, BrowseLink.Network, 49)!.Path);
        Assert.Null(SearchService.BrowseList(RequestType.Movie, BrowseLink.Network, 49));
        Assert.Null(SearchService.BrowseList(RequestType.Movie, "Keyword", 1));
        Assert.Null(SearchService.BrowseList(RequestType.Movie, BrowseLink.Genre, 0));
    }

    /// <summary>
    /// Home rows show in their order without the hidden ones, and with no order in the built-in order.
    /// True: the home page shows the rows as Settings arranged them.
    /// False: a hidden row would show, or the order would be lost.
    /// </summary>
    [Fact]
    public void Ordered_OrderAndHidden_AreHeldTo()
    {
        var rows = new[] { ("TrendingShows", "Trending shows"), ("TrendingMovies", "Trending movies"), ("PopularMovies", "Popular movies"), ("Custom-a", "Anime") };

        var shown = SearchService.Ordered(rows, new List<string> { "Custom-a", "PopularMovies", "Gone" }, new[] { "TrendingMovies" });

        Assert.Equal(new[] { "Custom-a", "PopularMovies", "TrendingShows" }, shown.Select(r => r.Key));
        Assert.Equal(rows.Select(r => r.Item1), SearchService.Ordered(rows, new List<string>(), Array.Empty<string>()).Select(r => r.Key));
    }

    /// <summary>
    /// The same open requests are indexed once, and another list afresh.
    /// True: marking a page of titles reads the open requests' index without building it again.
    /// False: every page would index every open request, or a new request would be missed.
    /// </summary>
    [Fact]
    public void IndexOf_SameList_IsIndexedOnce()
    {
        IReadOnlyList<MediaRequest> open = new List<MediaRequest> { Request(RequestType.Movie, ("Tmdb", "603")), Request(RequestType.Series, ("Tmdb", "1399"), ("Tvdb", "121361")) };
        IReadOnlyList<MediaRequest> after = new List<MediaRequest>(open) { Request(RequestType.Series, ("Tvdb", "81189")) };

        var index = SearchService.IndexOf(open);
        var next = SearchService.IndexOf(after);

        Assert.Same(index, SearchService.IndexOf(open));
        Assert.Single(index.ById["Movie|TMDB|603"]);
        Assert.False(index.ShowsByTvdbOnly);
        Assert.NotSame(index, next);
        Assert.True(next.ShowsByTvdbOnly);
    }

    /// <summary>
    /// The database hands out one list until a request changes, so the index is the same.
    /// True: the index is built once per change of the requests.
    /// False: the index would be built on every read.
    /// </summary>
    [Fact]
    public void IndexOf_DatabasesOpenRequests_IsTheSameUntilAChange()
    {
        using var db = new RequestDatabase(Path.Join(_folder, "requests.db"), NullLogger<RequestDatabase>.Instance);

        var open = db.OpenRequests();

        Assert.Same(SearchService.IndexOf(open), SearchService.IndexOf(db.OpenRequests()));
    }

    /// <summary>
    /// A requester may cancel a request Radarr dropped, and not one still in Radarr.
    /// True: the card offers Cancel only where Remove would succeed.
    /// False: the card would offer a cancel the server refuses, or hide one it allows.
    /// </summary>
    [Fact]
    public void CanCancel_DroppedByRadarr_IsTheRequesters()
    {
        var mine = Guid.NewGuid();
        var dropped = Sent(mine);
        dropped.RemovedFromArr = true;
        var requester = new UserAccess { UserId = mine };

        Assert.True(SearchService.CanCancel(dropped, requester));
        Assert.False(SearchService.CanCancel(Sent(mine), requester));
    }

    /// <summary>
    /// The first use after a quiet spell wakes the warmer, and further use while in use wakes nothing.
    /// True: the rows are refreshed when someone comes back, once.
    /// False: the warmer would sleep through a visit, or wake on every call.
    /// </summary>
    [Fact]
    public void Used_AfterAQuietSpell_WakesTheWarmer()
    {
        WithSearch(search =>
        {
            Assert.False(search.InUse);
            var woken = search.Woken;
            Assert.False(woken.IsCompleted);

            search.Used();
            Assert.True(woken.IsCompleted);
            Assert.True(search.InUse);

            var next = search.Woken;
            search.Used();
            Assert.False(next.IsCompleted);
            return Task.FromResult(0);
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The read ahead of the first visit, and the work it hands to other threads, is not a use.
    /// True: reading the rows ahead does not keep the warmer awake on its own.
    /// False: the warmer would wake itself forever.
    /// </summary>
    [Fact]
    public async Task Unnoticed_ReadAhead_IsNotAUse()
    {
        await WithSearch(async search =>
        {
            var woken = search.Woken;

            var read = await SearchService.Unnoticed(async () =>
            {
                search.Used();
                await Task.Run(search.Used).ConfigureAwait(false);
                return 1;
            });

            Assert.Equal(1, read);
            Assert.False(search.InUse);
            Assert.False(woken.IsCompleted);
            search.Used();
            Assert.True(search.InUse);
            Assert.True(woken.IsCompleted);
            return 0;
        });
    }

    /// <summary>
    /// Recommendations that more watched titles share come first.
    /// True: a title recommended by two watched titles leads the row.
    /// False: the row would follow one title's list alone.
    /// </summary>
    [Fact]
    public void Ranked_SharedRecommendations_ComeFirst()
    {
        var lists = new List<IReadOnlyList<SearchResult>>
        {
            new[] { Title(RequestType.Movie, "1"), Title(RequestType.Movie, "2"), Title(RequestType.Movie, "3") },
            new[] { Title(RequestType.Movie, "4"), Title(RequestType.Movie, "3") }
        };

        var ranked = SearchService.Ranked(lists, new HashSet<string>(), new[] { RequestType.Movie });

        Assert.Equal(new[] { "3", "1", "4", "2" }, ranked.Select(r => r.Title));
    }

    /// <summary>
    /// Recommendations leave out watched titles, unwanted types, and titles without a TMDb id.
    /// True: the row offers only new titles of a type the viewer may request.
    /// False: the row would recommend what the viewer watched, or a title it cannot open.
    /// </summary>
    [Fact]
    public void Ranked_WatchedAndUnwanted_AreLeftOut()
    {
        var lists = new List<IReadOnlyList<SearchResult>>
        {
            new[] { Title(RequestType.Movie, "1"), Title(RequestType.Series, "1"), Title(RequestType.Movie, "2"), new SearchResult { Type = RequestType.Movie, Title = "none" } }
        };

        var ranked = SearchService.Ranked(lists, new HashSet<string> { "Movie|2" }, new[] { RequestType.Movie });

        Assert.Equal("1", Assert.Single(ranked).Title);
        Assert.Equal(RequestType.Movie, ranked[0].Type);
    }

    /// <summary>
    /// Recently added is read a page at a time, as library titles.
    /// True: the row pages through the library with its total and next start.
    /// False: the row would read everything at once, or lose its place.
    /// </summary>
    [Fact]
    public async Task ViewerListAsync_RecentlyAdded_ReadsAPageAtATime()
    {
        _newestMovies = Enumerable.Range(1, 5).Select(n => (BaseItem)AddMovie("Movie " + n, n)).ToList();

        var first = await WithSearch(s => s.ViewerListAsync("RecentlyAdded", Viewer(), 0, 2, CancellationToken.None));
        var last = await WithSearch(s => s.ViewerListAsync("RecentlyAdded", Viewer(), 4, 2, CancellationToken.None));

        Assert.NotNull(first);
        Assert.Equal(new[] { "Movie 1", "Movie 2" }, first!.Items.Select(i => i.Title));
        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Next);
        Assert.All(first.Items, i => Assert.NotNull(i.LibraryItemId));
        Assert.Equal("Movie 5", Assert.Single(last!.Items).Title);
        Assert.Null(last.Next);
    }

    /// <summary>
    /// Recently added holds only the types the viewer may request and the filter picks.
    /// True: a viewer who may not request movies sees only shows there.
    /// False: the row would offer titles of a type the viewer cannot request.
    /// </summary>
    [Fact]
    public async Task ViewerListAsync_RecentlyAdded_HoldsTheViewersTypes()
    {
        var andor = AddShow("Andor");
        _newestMovies = new List<BaseItem> { AddMovie("Dune", 1) };
        _newestEpisodes = new List<BaseItem> { Episode(andor, 2) };
        var showsOnly = TitleFilter.Parse(null, null, "Series", null);

        var noMovies = await WithSearch(s => s.ViewerListAsync("RecentlyAdded", Viewer(movies: false), 0, 10, CancellationToken.None));
        var filtered = await WithSearch(s => s.ViewerListAsync("RecentlyAdded", Viewer(), 0, 10, CancellationToken.None, showsOnly));
        var neither = await WithSearch(s => s.ViewerListAsync("RecentlyAdded", Viewer(movies: false, series: false), 0, 10, CancellationToken.None));

        Assert.Equal("Andor", Assert.Single(noMovies!.Items).Title);
        Assert.Equal("Andor", Assert.Single(filtered!.Items).Title);
        Assert.Empty(neither!.Items);
    }

    /// <summary>
    /// Recommended for you and any other key are not found without TMDb.
    /// True: a server without a TMDb key answers those rows with nothing.
    /// False: the row would fail or show a TMDb row through the viewer's list.
    /// </summary>
    [Fact]
    public async Task ViewerListAsync_WithoutTmdb_FindsNoOtherRow()
    {
        // Settings of their own, so no TMDb key from an earlier test applies.
        _ = Harness;

        Assert.Null(await WithSearch(s => s.ViewerListAsync("RecommendedForYou", Viewer(), 0, 10, CancellationToken.None)));
        Assert.Null(await WithSearch(s => s.ViewerListAsync("TrendingShows", Viewer(), 0, 10, CancellationToken.None)));
    }

    /// <summary>
    /// Without TMDb, the home rows are recently added alone.
    /// True: a server without a TMDb key still shows what arrived.
    /// False: the home page would be empty, or show rows it cannot fill.
    /// </summary>
    [Fact]
    public async Task DiscoverAsync_WithoutTmdb_IsRecentlyAddedAlone()
    {
        _ = Harness;
        BaseItem.LibraryManager = _libraryManager;
        _newestMovies = new List<BaseItem> { AddMovie("Dune", 1) };

        var rows = await WithSearch(s => s.DiscoverAsync(Viewer(), CancellationToken.None));

        var row = Assert.Single(rows);
        Assert.Equal("RecentlyAdded", row.Key);
        Assert.Equal("Dune", Assert.Single(row.Items).Title);
    }

    /// <summary>
    /// The rows are kept in the cache folder and read without TMDb after a restart, until a row's settings change.
    /// True: the first visit after a restart shows the rows with nothing asked of TMDb.
    /// False: every restart would ask TMDb for every row, or a changed row would show its old list.
    /// </summary>
    [Fact]
    public async Task RefreshStoredAsync_AfterARestart_RowsAreReadWithoutTmdb()
    {
        Harness.Plugin.MutateConfiguration(c =>
        {
            c.Requests.TmdbApiKey = "key";
            c.Requests.HomeRows = new List<HomeRow> { new() { Id = "a", Title = "Drama", Type = RequestType.Series, Kind = "Genre", Ids = "18" } };
            return false;
        });
        using var http = new OutsideServers();
        using (var arr = Arr(http))
        using (var tmdb = Tmdb(http))
        {
            Assert.True(await Search(arr, tmdb).RefreshStoredAsync(CancellationToken.None) > 0);
        }

        http.Asked.Clear();
        using var arrAfter = Arr(http);
        using var after = Tmdb(http);
        after.LoadStore();
        var search = Search(arrAfter, after);
        var list = search.ListFor(SearchService.CustomRowPrefix + "a")!;
        var page = await after.PageAsync(list.Type, list.Path, list.Query, 1, CancellationToken.None, list.Row);
        var upcoming = search.ListFor("UpcomingShows")!;
        var dated = new Dictionary<string, string>(upcoming.Query) { ["first_air_date.gte"] = "2000-01-01" };

        Assert.True(after.StoreIsCurrent);
        Assert.Equal("Show", Assert.Single(page!.Items).Title);
        Assert.Equal(new[] { "Drama" }, page.Items[0].Genres);
        Assert.Equal("500", page.Items[0].ProviderIds[ProviderIds.Tvdb]);
        Assert.Equal(3, await after.SeasonCountAsync(new Dictionary<string, string> { [ProviderIds.Tmdb] = "7" }, CancellationToken.None));
        Assert.Single((await after.PageAsync(upcoming.Type, upcoming.Path, dated, 1, CancellationToken.None, upcoming.Row))!.Items);
        Assert.Empty(http.Asked);

        // Changed in the settings, it is no longer the list that was stored, so it is read from TMDb.
        Harness.Plugin.MutateConfiguration(c => { c.Requests.HomeRows[0].Ids = "35"; return false; });
        var changed = search.ListFor(SearchService.CustomRowPrefix + "a")!;
        Assert.NotNull(await after.PageAsync(changed.Type, changed.Path, changed.Query, 1, CancellationToken.None, changed.Row));
        Assert.Equal(new[] { "api.themoviedb.org/3/discover/tv" }, http.Asked);
    }

    /// <summary>
    /// The rows' posters are kept ahead of the first visit, once, and the refresh lets other images go.
    /// True: the home page's posters load from the server's cache, and shown backdrops do not pile up.
    /// False: the first visit would fetch every poster, or the cache would grow without end.
    /// </summary>
    [Fact]
    public async Task KeepPostersAsync_Rows_KeepsTheirPostersOnce()
    {
        Harness.Plugin.MutateConfiguration(c => { c.Requests.TmdbApiKey = "key"; return false; });
        using var http = new OutsideServers();
        using var artwork = new ArtworkCache(http, InterfaceStub.Of<IServerApplicationPaths>(("get_CachePath", _ => _folder)), NullLogger<ArtworkCache>.Instance);
        using var arr = Arr(http);
        using var tmdb = Tmdb(http);
        var search = Search(arr, tmdb, artwork);

        Assert.True(await search.RefreshStoredAsync(CancellationToken.None) > 0);
        Assert.Equal(new[] { "image.tmdb.org/t/p/w342/p7.jpg" }, http.Asked.Where(a => a.StartsWith("image.", StringComparison.Ordinal)));
        Assert.True(File.Exists(Path.Join(artwork.Folder, "w342", "p7.jpg")));

        http.Asked.Clear();
        Assert.Equal(1, await search.KeepPostersAsync(CancellationToken.None));
        Assert.Empty(http.Asked);

        Assert.NotNull(await artwork.GetAsync("w1280", "shown.jpg", CancellationToken.None));
        Assert.True(await search.RefreshStoredAsync(CancellationToken.None) > 0);
        Assert.False(File.Exists(Path.Join(artwork.Folder, "w1280", "shown.jpg")));
        Assert.True(File.Exists(Path.Join(artwork.Folder, "w342", "p7.jpg")));
    }

    private ArrClient Arr(OutsideServers http)
        => new(NullLogger<ArrClient>.Instance, InterfaceStub.Of<IApplicationPaths>(("get_CachePath", _ => _folder)), http);

    private TmdbDiscovery Tmdb(OutsideServers http)
        => new(
            http,
            InterfaceStub.Of<IPluginManager>(),
            InterfaceStub.Of<IServerConfigurationManager>(("get_ApplicationPaths", _ => InterfaceStub.Of<IServerApplicationPaths>(("get_CachePath", _ => _folder)))),
            NullLogger<TmdbDiscovery>.Instance);

    private SearchService Search(ArrClient arr, TmdbDiscovery tmdb, ArtworkCache? artwork = null)
        => new(arr, Matcher(), Harness.Db, tmdb, NullLogger<SearchService>.Instance, artwork);

    private async Task<T> WithSearch<T>(Func<SearchService, Task<T>> read)
    {
        using var db = new RequestDatabase(Path.Join(_folder, "search.db"), NullLogger<RequestDatabase>.Instance);
        using var arr = new ArrClient(InterfaceStub.Of<IHttpClientFactory>(), NullLogger<ArrClient>.Instance);
        using var tmdb = new TmdbDiscovery(InterfaceStub.Of<IHttpClientFactory>(), InterfaceStub.Of<IPluginManager>(), InterfaceStub.Of<IServerConfigurationManager>(), NullLogger<TmdbDiscovery>.Instance);
        return await read(new SearchService(arr, Matcher(), db, tmdb, NullLogger<SearchService>.Instance));
    }

    // The library as Jellyfin's database would answer the newest titles, already in the order asked for.
    private List<BaseItem> Answer(InternalItemsQuery query)
    {
        var kinds = query.IncludeItemTypes ?? Array.Empty<BaseItemKind>();
        if (query.IsPlayed == true || query.IsFavorite == true || query.OrderBy.Count == 0)
        {
            return new List<BaseItem>();
        }

        return kinds.Contains(BaseItemKind.Episode) ? _newestEpisodes : kinds.Contains(BaseItemKind.Movie) ? _newestMovies : new List<BaseItem>();
    }

    private LibraryMatcher Matcher() => new(_libraryManager, InterfaceStub.Of<IUserDataManager>(), InterfaceStub.Of<IUserManager>());

    private UserAccess Viewer(bool movies = true, bool series = true) => new() { User = _user, UserId = _user.Id, UserName = "viewer", CanRequest = true, Movies = movies, Series = series };

    private Movie AddMovie(string name, int daysAgo)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Name = name, DateCreated = DateTime.UtcNow.AddDays(-daysAgo) };
        _items.Add(movie);
        return movie;
    }

    private Series AddShow(string name)
    {
        var series = new Series { Id = Guid.NewGuid(), Name = name };
        _items.Add(series);
        return series;
    }

    private Episode Episode(Series series, int daysAgo)
    {
        var episode = new Episode { Id = Guid.NewGuid(), Name = series.Name + " episode", SeriesId = series.Id, DateCreated = DateTime.UtcNow.AddDays(-daysAgo) };
        _items.Add(episode);
        return episode;
    }

    private static SearchResult Title(RequestType type, string tmdb) => new() { Type = type, Title = tmdb, ProviderIds = { ["Tmdb"] = tmdb } };

    private static MediaRequest Request(RequestType type, params (string Key, string Value)[] ids)
        => new() { Type = type, ProviderIds = ids.ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase) };

    private static MediaRequest Sent(Guid user) => new()
    {
        Id = Guid.NewGuid(),
        Type = RequestType.Movie,
        Title = "The Matrix",
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = "603" },
        UserId = user,
        Status = RequestStatus.Approved,
        ArrId = 12,
        ServerId = "radarr"
    };
}
