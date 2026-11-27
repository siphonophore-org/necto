using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// TMDb's lists become search results, and TMDb is asked a limited number of questions at once, each once, with its
/// errors and rate limits respected and its answers kept across a restart. The key lives in the loaded plugin's
/// settings, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public sealed class TmdbDiscoveryTests : IDisposable
{
    private static readonly Dictionary<int, string> Genres = new() { [18] = "Drama", [878] = "Science Fiction", [10765] = "Sci-Fi & Fantasy" };

    private readonly string _cache = Path.Join(Path.GetTempPath(), "necto-tmdb-" + Guid.NewGuid().ToString("N"));
    private RequestHarness? _harness;

    private RequestHarness Harness
    {
        get
        {
            if (_harness is null)
            {
                _harness = new RequestHarness();
                _harness.Plugin.MutateConfiguration(c => { c.Requests.TmdbApiKey = "key"; return false; });
            }

            return _harness;
        }
    }

    public void Dispose()
    {
        _harness?.Dispose();
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    /// <summary>
    /// A movie from a TMDb list becomes a search result with its ids, artwork, genres, and rating.
    /// True: TMDb's rows show each movie as the page expects.
    /// False: a movie would show without its poster, genres, or id.
    /// </summary>
    [Fact]
    public void ToResult_Movie_IsASearchResult()
    {
        var item = JsonNode.Parse("""{"id":438631,"title":"Dune","original_title":"Dune","release_date":"2021-09-15","overview":"Paul.","poster_path":"/d.jpg","backdrop_path":"/b.jpg","genre_ids":[878,18,999],"vote_average":7.84,"original_language":"en"}""")!.AsObject();

        var result = TmdbDiscovery.ToResult(RequestType.Movie, item, Genres)!;

        Assert.Equal("Dune", result.Title);
        Assert.Null(result.OriginalTitle);
        Assert.Equal(2021, result.Year);
        Assert.Equal("438631", result.ProviderIds[ProviderIds.Tmdb]);
        Assert.Equal("https://image.tmdb.org/t/p/w342/d.jpg", result.PosterUrl);
        Assert.Equal("https://image.tmdb.org/t/p/w1280/b.jpg", result.BackdropUrl);
        Assert.Equal(new[] { "Science Fiction", "Drama" }, result.Genres);
        Assert.Equal(7.8, result.TmdbRating);
        Assert.Equal("en", result.Language);
    }

    /// <summary>
    /// A show uses its name and first air date, and one without an id is none.
    /// True: TMDb's show rows read as shows.
    /// False: a show would show without its title or year, or an entry without an id would break a row.
    /// </summary>
    [Fact]
    public void ToResult_Show_UsesItsNameAndFirstAirDate()
    {
        var item = JsonNode.Parse("""{"id":95396,"name":"Severance","original_name":"Severance","first_air_date":"2022-02-17","poster_path":null,"genre_ids":[18,10765]}""")!.AsObject();

        var result = TmdbDiscovery.ToResult(RequestType.Series, item, Genres)!;

        Assert.Equal(RequestType.Series, result.Type);
        Assert.Equal("Severance", result.Title);
        Assert.Equal(new[] { "Drama", "Sci-Fi & Fantasy" }, result.Genres);
        Assert.Equal(2022, result.Year);
        Assert.Null(result.PosterUrl);
        Assert.Null(TmdbDiscovery.ToResult(RequestType.Series, JsonNode.Parse("""{"name":"No id"}""")!.AsObject(), Genres));
    }

    /// <summary>
    /// No more than the limit of questions are asked of TMDb at once.
    /// True: a busy page waits its turn rather than flooding TMDb.
    /// False: TMDb would rate limit the server.
    /// </summary>
    [Fact]
    public async Task ChoicesAsync_ManyAtOnce_AreHeldToTheLimit()
    {
        _ = Harness;
        using var http = new HeldTmdb { Hold = true };
        using var tmdb = Tmdb(http);

        var asking = Enumerable.Range(0, 40).Select(i => tmdb.ChoicesAsync("Keyword", RequestType.Movie, "keyword " + i, CancellationToken.None)).ToList();
        for (var waited = 0; http.Asked < TmdbDiscovery.MaxInFlight && waited < 100; waited++)
        {
            await Task.Delay(50);
        }

        await Task.Delay(300);
        Assert.Equal(TmdbDiscovery.MaxInFlight, http.MostAtOnce);
        Assert.Equal(TmdbDiscovery.MaxInFlight, http.Asked);

        http.Release.SetResult();
        var answers = await Task.WhenAll(asking);
        Assert.All(answers, a => Assert.Single(a));
        Assert.Equal(40, http.Asked);
        Assert.True(http.MostAtOnce <= TmdbDiscovery.MaxInFlight);
    }

    /// <summary>
    /// The same question asked at once is asked once.
    /// True: ten visitors typing the same word cost one question.
    /// False: TMDb would be asked the same thing ten times.
    /// </summary>
    [Fact]
    public async Task ChoicesAsync_SameQuestionAtOnce_IsAskedOnce()
    {
        _ = Harness;
        using var http = new HeldTmdb { Hold = true };
        using var tmdb = Tmdb(http);

        var asking = Enumerable.Range(0, 10).Select(_ => tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None)).ToList();
        await Task.Delay(200);
        http.Release.SetResult();
        await Task.WhenAll(asking);

        Assert.Equal(1, http.Asked);
    }

    /// <summary>
    /// An error from TMDb is not asked again straight away, and only that question waits.
    /// True: an outage costs one question per question, and others are still asked.
    /// False: every keystroke would ask TMDb again during an outage.
    /// </summary>
    [Fact]
    public async Task ChoicesAsync_Error_IsNotAskedAgainStraightAway()
    {
        _ = Harness;
        using var http = new HeldTmdb { Answer = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        using var tmdb = Tmdb(http);

        Assert.Empty(await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Empty(await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Equal(1, http.Asked);

        await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "drama", CancellationToken.None);
        Assert.Equal(2, http.Asked);
    }

    /// <summary>
    /// A rate limit pauses every question for as long as TMDb says.
    /// True: the server stops asking until TMDb's Retry-After has passed.
    /// False: the server would keep asking and stay limited.
    /// </summary>
    [Fact]
    public async Task ChoicesAsync_RateLimit_PausesEveryQuestion()
    {
        _ = Harness;
        using var http = new HeldTmdb
        {
            Answer = () =>
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return limited;
            }
        };
        using var tmdb = Tmdb(http);

        Assert.Empty(await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Empty(await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Empty(await tmdb.ChoicesAsync("Company", RequestType.Movie, "studio", CancellationToken.None));
        Assert.Equal(1, http.Asked);
    }

    /// <summary>
    /// Answers kept are counted by their size, and each caller reads its own copy.
    /// True: memory kept for TMDb stays within its limit, and callers cannot change each other's answers.
    /// False: the cache would grow without limit, or share one answer between callers.
    /// </summary>
    [Fact]
    public async Task KeptBytes_Answers_AreCountedBySize()
    {
        _ = Harness;
        using var http = new HeldTmdb();
        using var tmdb = Tmdb(http);
        Assert.Equal(0, tmdb.KeptBytes);

        await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None);
        var one = tmdb.KeptBytes;
        var again = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None))));

        Assert.True(one > 0);
        Assert.All(again, a => Assert.Equal("Anime", Assert.Single(a).Name));
        Assert.Equal(1, http.Asked);
        Assert.Equal(one, tmdb.KeptBytes);
    }

    /// <summary>
    /// What the page is used for is kept on disk until the next refresh, which keeps the rows and lets the rest go.
    /// True: a restart reads the answers from the cache folder, and old answers do not outlive a refresh.
    /// False: every restart would ask TMDb again, or the cache folder would only grow.
    /// </summary>
    [Fact]
    public async Task LoadStore_UntilTheNextRefresh_KeepsWhatWasUsed()
    {
        _ = Harness;
        using var http = new OutsideServers();
        using var arr = new ArrClient(NullLogger<ArrClient>.Instance, InterfaceStub.Of<IApplicationPaths>(("get_CachePath", _ => _cache)), http);
        using (var before = Tmdb(http))
        {
            Assert.Single(await before.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
            Assert.Single(http.Asked);
            Assert.True(before.KeptOnDisk > 0);
        }

        using var tmdb = Tmdb(http);
        tmdb.LoadStore();
        Assert.Single(await tmdb.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Single(http.Asked);

        // Written long enough ago to tell apart from what the refresh keeps.
        foreach (var file in Directory.GetFiles(Path.Join(_cache, "requests", "tmdb")))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        }

        using var again = Tmdb(http);
        var search = new SearchService(arr, new LibraryMatcher(InterfaceStub.Of<MediaBrowser.Controller.Library.ILibraryManager>(), InterfaceStub.Of<MediaBrowser.Controller.Library.IUserDataManager>(), InterfaceStub.Of<MediaBrowser.Controller.Library.IUserManager>()), Harness.Db, again, NullLogger<SearchService>.Instance);
        Assert.True(await search.RefreshStoredAsync(CancellationToken.None) > 0);
        http.Asked.Clear();
        Assert.Single(await again.ChoicesAsync("Keyword", RequestType.Movie, "anime", CancellationToken.None));
        Assert.Equal(new[] { "api.themoviedb.org/3/search/keyword" }, http.Asked);
        Assert.True(again.StoreIsCurrent);
        using var restarted = Tmdb(http);
        Assert.True(restarted.StoreIsCurrent);
    }

    private TmdbDiscovery Tmdb(HttpMessageHandler http)
        => new(
            (IHttpClientFactory)http,
            InterfaceStub.Of<IPluginManager>(),
            InterfaceStub.Of<IServerConfigurationManager>(("get_ApplicationPaths", _ => InterfaceStub.Of<IServerApplicationPaths>(("get_CachePath", _ => _cache)))),
            NullLogger<TmdbDiscovery>.Instance);

    // Answers TMDb's questions as told, counting how many are asked and how many at once.
    private sealed class HeldTmdb : HttpMessageHandler, IHttpClientFactory
    {
        private readonly object _lock = new();
        private int _now;

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Hold { get; set; }

        public Func<HttpResponseMessage>? Answer { get; set; }

        public int Asked { get; private set; }

        public int MostAtOnce { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Asked++;
                MostAtOnce = Math.Max(MostAtOnce, ++_now);
            }

            try
            {
                if (Hold)
                {
                    await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return Answer?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"results":[{"id":1,"name":"Anime"}]}""", Encoding.UTF8, "application/json") };
            }
            finally
            {
                lock (_lock)
                {
                    _now--;
                }
            }
        }
    }
}
