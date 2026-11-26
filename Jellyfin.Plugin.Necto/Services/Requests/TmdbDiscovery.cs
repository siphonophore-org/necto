using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// TMDb's lists for discovery (trending, upcoming, and titles by genre, studio or network) and its recommendations,
/// asked of TMDb directly with the key in the plugin's settings, else the one in Jellyfin's TMDb settings, else the one
/// Jellyfin ships with. Every answer is kept in one cache, in memory and in the cache folder, so it's there after a
/// restart: the daily task fills it ahead of time with the home rows' first pages, their shows and the genre lists
/// (RefreshStoreAsync), whatever the page is used for is kept alongside as it's asked, and each refresh then lets go
/// of everything kept before it. Lists follow the server's metadata language and country.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class TmdbDiscovery : IDisposable
{
    /// <summary>
    /// How long an answer is used at most: a day between the daily refreshes, which let go of everything kept before
    /// them, with room for one that runs late.
    /// </summary>
    internal static readonly TimeSpan KeptFor = TimeSpan.FromDays(2);

    /// <summary>How many titles a TMDb list page holds.</summary>
    internal const int PageSize = 20;

    /// <summary>The last page TMDb serves of any list.</summary>
    internal const int LastPage = 500;

    private const string ApiBase = "https://api.themoviedb.org/3/";
    private const string ImageBase = "https://image.tmdb.org/t/p/";

    // Jellyfin's own TMDb plugin, which holds the key an administrator may set, and the built-in key it falls back to.
    private const string TmdbPluginType = "MediaBrowser.Providers.Plugins.Tmdb.Plugin";
    private const string TmdbUtilsType = "MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils, MediaBrowser.Providers";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginManager _pluginManager;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<TmdbDiscovery> _logger;
    // TMDb's answers until the next refresh lets go of them, and those being fetched now, so callers asking at once share one fetch. Each
    // answer is kept as a JsonElement, which is compact and safe to read from many threads at once, and handed to each
    // caller as a JsonObject of its own over it: a JsonObject fills itself in as it's first read, which two threads
    // reading the same one at once could break. The bytes kept in memory are counted, so they stay within MaxKeptBytes:
    // an answer let go of from memory is still in the cache folder (see TmdbDiscovery.Kept.cs).
    private readonly ConcurrentDictionary<string, KeptAnswer> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<JsonElement?>>> _fetching = new(StringComparer.Ordinal);
    private long _keptBytes;
    private int _trimming;
    // Questions TMDb just failed to answer (rate limited, its own error, or no answer at all), until when they're not
    // asked again, so viewers opening the same page don't each ask again into a rate limit. And until when TMDb asked
    // not to be asked anything (a 429's Retry-After).
    private readonly ConcurrentDictionary<string, DateTime> _failed = new(StringComparer.Ordinal);
    private long _pausedUntilTicks;
    // At most MaxInFlight questions to TMDb at once, started at about RequestsPerSecond, by every page and task together:
    // TMDb limits how fast it's asked, and a page of lists can otherwise ask dozens of questions at once.
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight);
    private readonly object _bucket = new();
    private double _tokens = Burst;
    private long _filledAt = System.Diagnostics.Stopwatch.GetTimestamp();
    // Jellyfin's TMDb key as last read, and when: read again a minute later, so a changed key is picked up. And the
    // server's own key, as stored (encrypted) and as read, so it's decrypted once per change.
    // Records, each swapped whole, so a read never pairs one call's fields with another's.
    private ReadKey? _apiKey;
    private OwnKey? _ownKey;
    private readonly ConcurrentDictionary<string, string?> _tvdbIds = new(StringComparer.Ordinal);
    private int _warned;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbDiscovery"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="pluginManager">Jellyfin's plugins, for its TMDb settings.</param>
    /// <param name="serverConfig">The server's settings, for its metadata language and country.</param>
    /// <param name="logger">The logger.</param>
    public TmdbDiscovery(IHttpClientFactory httpClientFactory, IPluginManager pluginManager, IServerConfigurationManager serverConfig, ILogger<TmdbDiscovery> logger)
    {
        _httpClientFactory = httpClientFactory;
        _pluginManager = pluginManager;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    /// <summary>The most questions asked of TMDb at once.</summary>
    internal const int MaxInFlight = 16;

    /// <summary>How many questions a second TMDb is asked at most, over time; under its own limit of about 50.</summary>
    internal const double RequestsPerSecond = 40;

    /// <summary>How long a question TMDb failed to answer (its own error, or no answer) isn't asked again.</summary>
    internal static readonly TimeSpan FailedFor = TimeSpan.FromSeconds(5);

    // The most questions waiting to be asked of TMDb at once.
    private const int MaxWaiting = 400;

    // How long a question TMDb refused the key for isn't asked again.
    private static readonly TimeSpan RefusedFor = TimeSpan.FromMinutes(1);
    private int _warnedRefused;

    /// <summary>The most bytes of TMDb's answers kept in memory.</summary>
    internal const long MaxKeptBytes = 64L * 1024 * 1024;

    // How many questions may start together after a quiet spell, before the rate applies.
    private const double Burst = 10;

    // The longest a 429's Retry-After is honoured for, and how long one without it pauses.
    private static readonly TimeSpan LongestPause = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(10);

    /// <summary>Gets a value indicating whether TMDb can be asked: Jellyfin has a key for it.</summary>
    public bool Available => ApiKey() is not null;

    /// <summary>
    /// Gets a page of a TMDb list of movies or shows, as search results. A show carries its TVDB id too when it's already
    /// known; none is looked up here, as the library, Sonarr's library read and requests all match a show by its TMDb id,
    /// and where only a TVDB id will do (a request, a show's page) it's added then.
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="path">The list, such as trending/tv/week or discover/movie.</param>
    /// <param name="query">The list's own parameters, such as with_genres.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="row">The home row the list is, whose stored pages are read first.</param>
    /// <returns>The page, or null when TMDb couldn't be asked.</returns>
    public async Task<TmdbPage?> PageAsync(RequestType type, string path, IReadOnlyDictionary<string, string> query, int page, CancellationToken cancellationToken, string? row = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        // TMDb lists no further than its last page: past it there's nothing, not that page again.
        if (page > LastPage)
        {
            return new TmdbPage(new List<SearchResult>(), LastPage * PageSize, LastPage);
        }

        var parameters = new Dictionary<string, string>(query, StringComparer.Ordinal) { ["page"] = Math.Max(page, 1).ToString(CultureInfo.InvariantCulture) };
        // A built-in home row's first pages are kept under the row itself by the daily refresh, as the list behind one
        // changes with the date (Upcoming); further pages, and every other list, under what TMDb is asked.
        var body = RowPage(row, page) ?? await GetAsync(path, parameters, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var genres = await GenresAsync(type, cancellationToken).ConfigureAwait(false);
        var items = (body["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(item => ToResult(type, item, genres))
            .OfType<SearchResult>()
            .ToList();
        if (type == RequestType.Series)
        {
            // Only those already known: a page of shows would otherwise ask TMDb once for each.
            foreach (var item in items)
            {
                if (KnownTvdbId(item.ProviderIds[ProviderIds.Tmdb]) is { } tvdb)
                {
                    item.ProviderIds[ProviderIds.Tvdb] = tvdb;
                }
            }
        }

        return new TmdbPage(items, Int(body["total_results"]) ?? items.Count, Math.Min(Int(body["total_pages"]) ?? 1, LastPage));
    }

    /// <summary>Gets TMDb's recommendations for a title, one page.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="tmdbId">The title's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The titles, or null when TMDb couldn't be asked.</returns>
    public Task<TmdbPage?> RecommendationsAsync(RequestType type, string tmdbId, CancellationToken cancellationToken)
        => PageAsync(type, (type == RequestType.Movie ? "movie/" : "tv/") + Uri.EscapeDataString(tmdbId) + "/recommendations", new Dictionary<string, string>(), 1, cancellationToken);

    // A title's details with its credits and age ratings, in one answer: its page, what it can be browsed
    // by and its genres all read this one. A show's credits are every season's, not only the latest's.
    // A show's every-season credits can be large, so only its page asks for them; its links and genres read it without.
    // Its page also asks for its videos, for a trailer: in the server's language, else English, else any.
    private Task<JsonObject?> TitleAsync(RequestType type, string tmdbId, CancellationToken cancellationToken, bool credits = false, bool fresh = false)
        => GetAsync(TitlePath(type, tmdbId), TitleQuery(type, credits), cancellationToken, fresh);

    private static string TitlePath(RequestType type, string tmdbId) => (type == RequestType.Movie ? "movie/" : "tv/") + Uri.EscapeDataString(tmdbId);

    private Dictionary<string, string> TitleQuery(RequestType type, bool credits)
    {
        var query = new Dictionary<string, string>
        {
            ["append_to_response"] = (type == RequestType.Movie ? "credits,release_dates" : credits ? "aggregate_credits,content_ratings,external_ids" : "content_ratings,external_ids") + ",keywords" + (credits ? ",videos" : string.Empty)
        };
        if (credits)
        {
            query["include_video_language"] = string.Join(',', new[] { Locale().Language?.Split('-')[0], "en", "null" }.OfType<string>().Distinct());
        }

        return query;
    }

    // A title's trailer on YouTube: an official trailer first, then any trailer, then a teaser, in the server's
    // language before English.
    private string? Trailer(JsonObject body)
    {
        var language = Locale().Language?.Split('-')[0];
        var key = ((body["videos"] as JsonObject)?["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(v => Text(v["site"]) == "YouTube" && Text(v["type"]) is "Trailer" or "Teaser" && Text(v["key"]) is { } k && YouTubeKey().IsMatch(k))
            .OrderBy(v => Text(v["type"]) == "Trailer" ? 0 : 1)
            .ThenBy(v => v["official"]?.GetValueKind() == JsonValueKind.True ? 0 : 1)
            .ThenBy(v => Text(v["iso_639_1"]) == language ? 0 : Text(v["iso_639_1"]) == "en" ? 1 : 2)
            .Select(v => Text(v["key"]))
            .FirstOrDefault();
        return key is null ? null : "https://www.youtube.com/watch?v=" + key;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9_-]{6,20}$")]
    private static partial System.Text.RegularExpressions.Regex YouTubeKey();

    /// <summary>
    /// Gets a title's page details from TMDb in one request: its tagline, cast and crew, studios or networks, genres,
    /// age rating in the server's country, release date, rating and, for an ended show, its last year.
    /// </summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The details; null when TMDb can't be asked, doesn't know the title, or didn't answer.</returns>
    public async Task<TmdbTitle?> TitleDetailsAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        if (!Available || await TmdbIdAsync(type, ids, cancellationToken).ConfigureAwait(false) is not { } tmdbId
            || await TitleAsync(type, tmdbId, cancellationToken, credits: true).ConfigureAwait(false) is not { Count: > 0 } body)
        {
            return null;
        }

        // A show's aggregate credits list each person once, with their roles and jobs across its episodes; the most
        // episodes first, for its crew.
        var isMovie = type == RequestType.Movie;
        var credits = body[isMovie ? "credits" : "aggregate_credits"] as JsonObject;
        TmdbCredit? Credit(JsonObject p, string? role)
            => Text(p["name"]) is { } name ? new TmdbCredit(name, role, Image(p["profile_path"], "w185"), Int(p["id"])?.ToString(CultureInfo.InvariantCulture)) : null;
        // A show's person's character or job: the one of most episodes, and Writer over the rest of their writing jobs.
        string? First(JsonObject p, string list, string field)
            => isMovie ? Text(p[field]) : (p[list] as JsonArray)?.OfType<JsonObject>()
                .OrderByDescending(r => Text(r[field]) == "Writer")
                .ThenByDescending(r => Int(r["episode_count"]) ?? 0)
                .Select(r => Text(r[field])).FirstOrDefault(t => t is { Length: > 0 });
        var crew = (credits?["crew"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .OrderByDescending(p => isMovie ? 0 : Int(p["total_episode_count"]) ?? 0)
            .ToList();
        var creators = (body["created_by"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(p => Credit(p, "Creator")).OfType<TmdbCredit>();
        var certification = Certification(type, body);

        var lastAired = Text(body["last_air_date"]);
        return new TmdbTitle(
            Text(body[type == RequestType.Movie ? "title" : "name"]),
            Text(body["tagline"]) is { Length: > 0 } tagline ? tagline : null,
            (credits?["cast"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .OrderBy(p => Int(p["order"]) ?? int.MaxValue)
                .Select(p => Credit(p, First(p, "roles", "character"))).OfType<TmdbCredit>().ToList(),
            crew.Where(p => isMovie ? Text(p["job"]) == "Director" : (p["jobs"] as JsonArray)?.OfType<JsonObject>().Any(j => Text(j["job"]) == "Director") == true)
                .Select(p => Credit(p, "Director")).OfType<TmdbCredit>().DistinctBy(c => c.Name).ToList(),
            creators.Concat(crew.Where(p => Text(p["department"]) == "Writing").Select(p => Credit(p, First(p, "jobs", "job")))).OfType<TmdbCredit>().DistinctBy(c => c.Name).ToList(),
            ((body[type == RequestType.Movie ? "production_companies" : "networks"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(c => Text(c["name"])).OfType<string>().ToList(),
            ((body["genres"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(g => Text(g["name"])).OfType<string>().ToList(),
            certification,
            Date(body[type == RequestType.Movie ? "release_date" : "first_air_date"]),
            body["vote_average"]?.GetValueKind() == JsonValueKind.Number && body["vote_average"]!.GetValue<double>() > 0 ? body["vote_average"]!.GetValue<double>() : null,
            type == RequestType.Series && Text(body["status"]) == "Ended" && DateTime.TryParse(lastAired, CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) ? last.Year : null)
        {
            TrailerUrl = Trailer(body),
            Traits = Traits(type, body),
            Collection = body["belongs_to_collection"] is JsonObject set && Int(set["id"]) is > 0 and var setId && Text(set["name"]) is { } setName
                ? new BrowseLink { Kind = BrowseLink.Collection, Id = setId, Name = setName }
                : null
        };
    }

    /// <summary>
    /// Finds movies or shows by title on TMDb, the best matches first, in one request: a show with its TVDB id too when
    /// it's already known (see PageAsync).
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="query">The title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The titles; null when TMDb can't be asked or didn't answer.</returns>
    public async Task<List<SearchResult>?> SearchAsync(RequestType type, string query, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return null;
        }

        // Search doesn't wait on shows' TVDB ids: where one is needed (a request, a show's seasons) it's added then.
        var page = await PageAsync(type, type == RequestType.Movie ? "search/movie" : "search/tv", new Dictionary<string, string> { ["query"] = query, ["include_adult"] = "false" }, 1, cancellationToken).ConfigureAwait(false);
        return page?.Items;
    }

    /// <summary>Gets how many seasons a show has on TMDb, specials left out.</summary>
    /// <param name="ids">The show's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The count; null when TMDb can't be asked or doesn't know the show.</returns>
    public async Task<int?> SeasonCountAsync(IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
        => Available && await TmdbIdAsync(RequestType.Series, ids, cancellationToken).ConfigureAwait(false) is { } tmdbId
            && await TitleAsync(RequestType.Series, tmdbId, cancellationToken).ConfigureAwait(false) is { Count: > 0 } body
            && Int(body["number_of_seasons"]) is > 0 and var count ? count : null;

    // A title's age rating in the server's country.
    private string? Certification(RequestType type, JsonObject body)
    {
        var country = Locale().Region ?? "US";
        return type == RequestType.Movie
            ? ((body["release_dates"] as JsonObject)?["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(r => Text(r["iso_3166_1"]) == country)
                .SelectMany(r => (r["release_dates"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                .Select(d => Text(d["certification"]))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))
            : ((body["content_ratings"] as JsonObject)?["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(r => Text(r["iso_3166_1"]) == country)
                .Select(r => Text(r["rating"]))
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
    }

    /// <summary>
    /// Gets what a request records and is checked by: a title's name, year, overview, artwork, every id it has (TMDb,
    /// IMDb and, for a show, TVDB), genres, age rating, whether it's out and, for a show, its seasons' episodes.
    /// </summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The title; null when TMDb can't be asked or doesn't know it.</returns>
    public async Task<TmdbDescription?> DescribeAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        if (!Available || await TmdbIdAsync(type, ids, cancellationToken).ConfigureAwait(false) is not { } tmdbId
            || await TitleAsync(type, tmdbId, cancellationToken).ConfigureAwait(false) is not { Count: > 0 } body
            || Text(body[type == RequestType.Movie ? "title" : "name"]) is not { } title)
        {
            return null;
        }

        var isMovie = type == RequestType.Movie;
        // The id as TMDb itself gives it, whatever it was asked by.
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderIds.Tmdb] = Int(body["id"]) is > 0 and var own ? own.ToString(CultureInfo.InvariantCulture) : tmdbId
        };
        var external = isMovie ? body : body["external_ids"] as JsonObject;
        if (Text(external?["imdb_id"]) is { } imdb)
        {
            known[ProviderIds.Imdb] = imdb;
        }

        if (!isMovie && Int(external?["tvdb_id"]) is > 0 and var tvdb)
        {
            known[ProviderIds.Tvdb] = tvdb.ToString(CultureInfo.InvariantCulture);
        }

        var released = Date(body[isMovie ? "release_date" : "first_air_date"]);
        return new TmdbDescription(
            title,
            released?.Year,
            Text(body["overview"]),
            Image(body["poster_path"], "w342"),
            Image(body["backdrop_path"], "w1280"),
            isMovie ? null : AiredEpisodes(body))
        {
            ProviderIds = ProviderIds.Clean(known),
            Genres = ((body["genres"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().Select(g => Text(g["name"])).OfType<string>().ToList(),
            Certification = Certification(type, body),
            Released = released is { } date ? date <= DateTime.UtcNow : null,
            Traits = Traits(type, body)
        };
    }

    // A title's keywords (a movie's under keywords, a show's under results), original language and collection.
    private static TitleTraits Traits(RequestType type, JsonObject body)
        => new(
            ((body["keywords"] as JsonObject)?[type == RequestType.Movie ? "keywords" : "results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(k => Int(k["id"])).OfType<int>().ToList(),
            Text(body["original_language"]),
            (body["belongs_to_collection"] as JsonObject) is { } set ? Int(set["id"]) : null);

    /// <summary>
    /// Gets a show's seasons for the request form, specials left out, each with the episodes aired so far (all of them
    /// for one not yet started).
    /// </summary>
    /// <param name="ids">The show's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The seasons; null when TMDb can't be asked or doesn't know the show.</returns>
    public async Task<List<SeasonInfo>?> SeasonsAsync(IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        if (!Available || await TmdbIdAsync(RequestType.Series, ids, cancellationToken).ConfigureAwait(false) is not { } tmdbId
            || await TitleAsync(RequestType.Series, tmdbId, cancellationToken).ConfigureAwait(false) is not { Count: > 0 } body)
        {
            return null;
        }

        var aired = AiredEpisodes(body);
        return (body["seasons"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(s => (Number: Int(s["season_number"]) ?? 0, Count: Int(s["episode_count"]), Item: s))
            .Where(s => s.Number > 0)
            .OrderBy(s => s.Number)
            .Select(s => new SeasonInfo
            {
                Number = s.Number,
                EpisodeCount = aired?.GetValueOrDefault(s.Number) is > 0 and var have ? have : s.Count is > 0 ? s.Count : null,
                Name = Text(s.Item["name"]),
                PosterUrl = Image(s.Item["poster_path"], "w342"),
                AirDate = Date(s.Item["air_date"])
            })
            .ToList();
    }

    /// <summary>Gets a season of a show and its episodes from TMDb, in one request.</summary>
    /// <param name="ids">The show's ids.</param>
    /// <param name="season">The season's number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The season; null when TMDb can't be asked or doesn't know it.</returns>
    public async Task<SeasonEpisodes?> EpisodesAsync(IReadOnlyDictionary<string, string> ids, int season, CancellationToken cancellationToken)
    {
        if (!Available || season < 0 || season > 1000 || await TmdbIdAsync(RequestType.Series, ids, cancellationToken).ConfigureAwait(false) is not { } tmdbId
            || await GetAsync("tv/" + Uri.EscapeDataString(tmdbId) + "/season/" + season.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false) is not { Count: > 0 } body)
        {
            return null;
        }

        return new SeasonEpisodes
        {
            Name = Text(body["name"]),
            Overview = Text(body["overview"]),
            PosterUrl = Image(body["poster_path"], "w342"),
            Episodes = (body["episodes"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(e => new EpisodeInfo
                {
                    Number = Int(e["episode_number"]) ?? 0,
                    Name = Text(e["name"]),
                    Overview = Text(e["overview"]),
                    AirDate = Date(e["air_date"]),
                    StillUrl = Image(e["still_path"], "w300"),
                    RuntimeMinutes = Int(e["runtime"]) is > 0 and var minutes ? minutes : null
                })
                .Where(e => e.Number > 0)
                .OrderBy(e => e.Number)
                .ToList()
        };
    }

    // The episodes of each season (specials left out) that have aired, by TMDb's latest aired episode: every one of
    // the seasons before it, as many of its own as have aired, and none of those after. Null when nothing has aired.
    private static Dictionary<int, int>? AiredEpisodes(JsonObject show)
    {
        var last = show["last_episode_to_air"] as JsonObject;
        if (Int(last?["season_number"]) is not { } lastSeason || lastSeason <= 0 || Int(last?["episode_number"]) is not { } lastEpisode || lastEpisode <= 0)
        {
            return null;
        }

        var counts = (show["seasons"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(s => (Number: Int(s["season_number"]) ?? 0, Count: Int(s["episode_count"]) ?? 0))
            .Where(s => s.Number > 0 && s.Number <= lastSeason && s.Count > 0)
            .ToDictionary(s => s.Number, s => s.Number == lastSeason ? Math.Min(lastEpisode, s.Count) : s.Count);
        return counts.Count > 0 ? counts : null;
    }

    /// <summary>
    /// Gets a person's biography, dates, birthplace and photo from TMDb, with every movie and show they're in or worked
    /// on, most popular first, in one request.
    /// </summary>
    /// <param name="tmdbId">The person's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The person; null when TMDb can't be asked or doesn't know them.</returns>
    public async Task<TmdbPerson?> PersonAsync(string tmdbId, CancellationToken cancellationToken)
    {
        if (!Available || !tmdbId.All(char.IsAsciiDigit)
            || await GetAsync("person/" + tmdbId, new Dictionary<string, string> { ["append_to_response"] = "combined_credits" }, cancellationToken).ConfigureAwait(false) is not { Count: > 0 } body
            || Text(body["name"]) is not { } name)
        {
            return null;
        }

        // Each title once, whether they acted in it, worked on it or both, the best known first (by how many rated it).
        // Left out: appearances as themselves, talk, news and reality shows, and anything without a poster.
        var movieGenres = await GenresAsync(RequestType.Movie, cancellationToken).ConfigureAwait(false);
        var showGenres = await GenresAsync(RequestType.Series, cancellationToken).ConfigureAwait(false);
        var credits = body["combined_credits"] as JsonObject;
        var titles = (credits?["cast"] as JsonArray ?? new JsonArray()).Concat(credits?["crew"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(c => Text(c["media_type"]) is "movie" or "tv" && Text(c["poster_path"]) is not null
                && !(Text(c["character"]) is { } role && SelfRole().IsMatch(role))
                && !(c["genre_ids"] as JsonArray ?? new JsonArray()).Select(g => Int(g)).Any(g => g is 10763 or 10764 or 10767))
            .DistinctBy(c => Text(c["media_type"]) + ":" + Int(c["id"]))
            .OrderByDescending(c => Int(c["vote_count"]) ?? 0)
            .Select(c => Text(c["media_type"]) == "movie" ? ToResult(RequestType.Movie, c, movieGenres) : ToResult(RequestType.Series, c, showGenres))
            .OfType<SearchResult>()
            .Take(60)
            .ToList();
        return new TmdbPerson(name, Text(body["biography"]), Date(body["birthday"]), Date(body["deathday"]), Text(body["place_of_birth"]), Image(body["profile_path"], "h632")) { Credits = titles };
    }

    // A part played as oneself: Self, Himself, Herself, Themselves, Self - Host and the like.
    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(self|himself|herself|themselves|themself)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SelfRole();

    // A TMDb date (yyyy-MM-dd), as UTC.
    private static DateTime? Date(JsonNode? node)
        => DateTime.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>Gets a collection of movies from TMDb, its movies in release order, in one request.</summary>
    /// <param name="id">The collection's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection; null when TMDb can't be asked or doesn't know it.</returns>
    public async Task<TmdbMovieSet?> CollectionAsync(int id, CancellationToken cancellationToken)
    {
        if (!Available || id <= 0
            || await GetAsync("collection/" + id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false) is not { Count: > 0 } body
            || Text(body["name"]) is not { } name)
        {
            return null;
        }

        var genres = await GenresAsync(RequestType.Movie, cancellationToken).ConfigureAwait(false);
        var parts = (body["parts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(item => ToResult(RequestType.Movie, item, genres))
            .OfType<SearchResult>()
            .OrderBy(r => r.ReleaseDate ?? DateTime.MaxValue)
            .ToList();
        return new TmdbMovieSet(id, name, Text(body["overview"]), Image(body["poster_path"], "w342"), Image(body["backdrop_path"], "w1280"), parts);
    }

    /// <summary>Gets the streaming services TMDb knows in the server's country for a type, most prominent first.</summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The services by id and name, with their logos; empty when TMDb can't be asked.</returns>
    public async Task<List<TmdbChoice>> ProvidersAsync(RequestType type, CancellationToken cancellationToken)
    {
        var region = Locale().Region ?? "US";
        if (!Available || await GetAsync(type == RequestType.Movie ? "watch/providers/movie" : "watch/providers/tv", new Dictionary<string, string> { ["watch_region"] = region }, cancellationToken).ConfigureAwait(false) is not { } body)
        {
            return new List<TmdbChoice>();
        }

        return (body["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(p => (Id: Int(p["provider_id"]), Name: Text(p["provider_name"]), Logo: Image(p["logo_path"], "w92"),
                Priority: Int((p["display_priorities"] as JsonObject)?[region]) ?? Int(p["display_priority"]) ?? int.MaxValue))
            .Where(p => p.Id is > 0 && p.Name is not null)
            .OrderBy(p => p.Priority)
            .Take(40)
            .Select(p => new TmdbChoice(p.Id!.Value, p.Name!, p.Logo))
            .ToList();
    }

    /// <summary>
    /// Finds what a home row can pick its titles by: a type's genres, TMDb's keywords or studios by name, or its
    /// streaming services. The blocklist also finds collections by name.
    /// </summary>
    /// <param name="kind">Genre, Keyword, Company, Collection or Provider.</param>
    /// <param name="type">Movies or shows.</param>
    /// <param name="query">For keywords, studios and collections, the name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The choices; empty when there are none or TMDb can't be asked.</returns>
    public async Task<List<TmdbChoice>> ChoicesAsync(string kind, RequestType type, string? query, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case "Genre":
                return (await GenresAsync(type, cancellationToken).ConfigureAwait(false)).OrderBy(g => g.Value, StringComparer.CurrentCulture).Select(g => new TmdbChoice(g.Key, g.Value, null)).ToList();
            case "Provider":
                return await ProvidersAsync(type, cancellationToken).ConfigureAwait(false);
            case "Keyword" or "Company" or "Collection" when (query ?? string.Empty).Trim() is { Length: >= 2 } name && Available:
                var body = await GetAsync(kind switch { "Keyword" => "search/keyword", "Company" => "search/company", _ => "search/collection" }, new Dictionary<string, string> { ["query"] = name }, cancellationToken).ConfigureAwait(false);
                return (body?["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    .Select(r => (Id: Int(r["id"]), Name: Text(r["name"]), Logo: Image(r["logo_path"], "w92")))
                    .Where(r => r.Id is > 0 && r.Name is not null)
                    .Take(20)
                    .Select(r => new TmdbChoice(r.Id!.Value, r.Name!, r.Logo))
                    .ToList();
            default:
                return new List<TmdbChoice>();
        }
    }

    /// <summary>Finds people by name on TMDb, the most popular first, in one request.</summary>
    /// <param name="query">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The people; null when TMDb can't be asked or didn't answer.</returns>
    public async Task<List<TmdbCredit>?> SearchPeopleAsync(string query, CancellationToken cancellationToken)
    {
        if (!Available || await GetAsync("search/person", new Dictionary<string, string> { ["query"] = query, ["include_adult"] = "false" }, cancellationToken).ConfigureAwait(false) is not { } body)
        {
            return null;
        }

        return (body["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(p => Text(p["name"]) is { } name ? new TmdbCredit(name, null, Image(p["profile_path"], "w185"), Int(p["id"])?.ToString(CultureInfo.InvariantCulture)) : null)
            .OfType<TmdbCredit>()
            .ToList();
    }

    /// <summary>
    /// Gets what a title can be browsed by: its genres, its studios and, for a show, its networks, each with the id TMDb
    /// lists titles by.
    /// </summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="tmdbId">The title's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links; empty when TMDb couldn't be asked.</returns>
    public async Task<List<BrowseLink>> LinksAsync(RequestType type, string tmdbId, CancellationToken cancellationToken)
    {
        var body = await TitleAsync(type, tmdbId, cancellationToken).ConfigureAwait(false);
        var links = new List<BrowseLink>();
        if (body is null)
        {
            return links;
        }

        void Add(string field, string kind)
        {
            foreach (var entry in (body[field] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (Int(entry["id"]) is > 0 and var id && Text(entry["name"]) is { } name)
                {
                    links.Add(new BrowseLink { Kind = kind, Id = id, Name = name });
                }
            }
        }

        Add("genres", BrowseLink.Genre);
        Add("production_companies", BrowseLink.Company);
        if (type == RequestType.Series)
        {
            Add("networks", BrowseLink.Network);
        }

        return links;
    }

    /// <summary>Gets a title's TMDb id: its own, or for a show known only by TVDB, TMDb's for that.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TMDb id, or null.</returns>
    public async Task<string?> TmdbIdAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        var clean = ProviderIds.Clean(ids);
        if (clean.TryGetValue(ProviderIds.Tmdb, out var tmdb))
        {
            return tmdb;
        }

        if (type == RequestType.Series && clean.TryGetValue(ProviderIds.Tvdb, out var tvdb) && Available)
        {
            return await ShowByTvdbAsync(tvdb, cancellationToken).ConfigureAwait(false);
        }

        return clean.TryGetValue(ProviderIds.Imdb, out var imdb) && Available
            ? await ByImdbAsync(type, imdb, cancellationToken).ConfigureAwait(false)
            : null;
    }

    // A movie or show's TMDb id by its IMDb id, from TMDb's find; null when TMDb knows none of that type.
    private async Task<string?> ByImdbAsync(RequestType type, string imdbId, CancellationToken cancellationToken)
    {
        var body = await GetAsync("find/" + Uri.EscapeDataString(imdbId), new Dictionary<string, string> { ["external_source"] = "imdb_id" }, cancellationToken).ConfigureAwait(false);
        return (body?[type == RequestType.Movie ? "movie_results" : "tv_results"] as JsonArray)?.OfType<JsonObject>().Select(s => Int(s["id"])).FirstOrDefault(id => id > 0)?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets a title's genres as TMDb names them, the names its lists and filters use.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="ids">The title's ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The genres, none for a title TMDb doesn't know, or null when TMDb didn't answer (ask again later).</returns>
    public async Task<List<string>?> GenresOfAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return null;
        }

        var clean = ProviderIds.Clean(ids);
        if (!clean.TryGetValue(ProviderIds.Tmdb, out var tmdbId))
        {
            // A show known by its TVDB id is looked up by it; anything else TMDb can't know.
            if (type != RequestType.Series || !clean.TryGetValue(ProviderIds.Tvdb, out var tvdb))
            {
                return new List<string>();
            }

            var found = await GetAsync("find/" + Uri.EscapeDataString(tvdb), new Dictionary<string, string> { ["external_source"] = "tvdb_id" }, cancellationToken).ConfigureAwait(false);
            if (found is null)
            {
                return null;
            }

            tmdbId = (found["tv_results"] as JsonArray)?.OfType<JsonObject>().Select(s => Int(s["id"])).FirstOrDefault(id => id > 0)?.ToString(CultureInfo.InvariantCulture);
            if (tmdbId is null)
            {
                return new List<string>();
            }
        }

        // The same details LinksAsync reads, so either fills the cache for the other.
        var body = await TitleAsync(type, tmdbId, cancellationToken).ConfigureAwait(false);
        return body is null ? null
            : (body["genres"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(g => Text(g["name"])).OfType<string>().ToList();
    }

    /// <summary>Gets every genre TMDb has for movies, shows or both, by name, for filtering by.</summary>
    /// <param name="types">The types.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The names, sorted; empty without TMDb.</returns>
    public async Task<List<string>> GenreNamesAsync(IEnumerable<RequestType> types, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(types);
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        if (Available)
        {
            foreach (var type in types.Distinct())
            {
                names.UnionWith((await GenresAsync(type, cancellationToken).ConfigureAwait(false)).Values);
            }
        }

        return names.ToList();
    }

    /// <summary>Finds a show's TMDb id from its TVDB id.</summary>
    /// <param name="tvdbId">The TVDB id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TMDb id, or null.</returns>
    public async Task<string?> ShowByTvdbAsync(string tvdbId, CancellationToken cancellationToken)
    {
        var body = await GetAsync("find/" + Uri.EscapeDataString(tvdbId), new Dictionary<string, string> { ["external_source"] = "tvdb_id" }, cancellationToken).ConfigureAwait(false);
        return (body?["tv_results"] as JsonArray)?.OfType<JsonObject>().Select(s => Int(s["id"])).FirstOrDefault(id => id > 0)?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the language and country lists are asked in: the server's metadata settings.</summary>
    /// <returns>The language, such as en-US, and the country, such as US, when set.</returns>
    internal (string? Language, string? Region) Locale()
    {
        var config = _serverConfig.Configuration;
        var language = (config.PreferredMetadataLanguage ?? string.Empty).Trim();
        var region = (config.MetadataCountryCode ?? string.Empty).Trim().ToUpperInvariant();
        return (language.Length == 0 ? null : region.Length == 2 && !language.Contains('-', StringComparison.Ordinal) ? language + "-" + region : language,
            region.Length == 2 ? region : null);
    }

    /// <summary>Maps one title of a TMDb list as a search result; null when it has no id or name.</summary>
    /// <param name="type">Movie or show.</param>
    /// <param name="item">The title as TMDb lists it.</param>
    /// <param name="genres">TMDb's genre names by id.</param>
    /// <returns>The title.</returns>
    internal static SearchResult? ToResult(RequestType type, JsonObject item, IReadOnlyDictionary<int, string> genres)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(genres);
        var id = Int(item["id"]);
        var title = Text(item[type == RequestType.Movie ? "title" : "name"]);
        if (id is not > 0 || title is null)
        {
            return null;
        }

        var released = Date(item[type == RequestType.Movie ? "release_date" : "first_air_date"]);
        var original = Text(item[type == RequestType.Movie ? "original_title" : "original_name"]);
        return new SearchResult
        {
            Type = type,
            Title = title,
            OriginalTitle = original is not null && !string.Equals(original, title, StringComparison.Ordinal) ? original : null,
            Year = released?.Year,
            ReleaseDate = released,
            Overview = Text(item["overview"]),
            PosterUrl = Image(item["poster_path"], "w342"),
            BackdropUrl = Image(item["backdrop_path"], "w1280"),
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderIds.Tmdb] = id.Value.ToString(CultureInfo.InvariantCulture) },
            Genres = (item["genre_ids"] as JsonArray ?? new JsonArray()).Select(g => Int(g)).OfType<int>().Select(g => genres.TryGetValue(g, out var name) ? name : null).OfType<string>().ToList(),
            TmdbRating = item["vote_average"]?.GetValueKind() == JsonValueKind.Number && item["vote_average"]!.GetValue<double>() > 0 ? Math.Round(item["vote_average"]!.GetValue<double>(), 1) : null,
            Language = Text(item["original_language"])
        };
    }

    // The key TMDb is asked with: the server's own when one is set in the settings, else Jellyfin's.
    private string? ApiKey()
    {
        if (RequestConfig.Instance is { } plugin && plugin.ReadConfiguration(c => c.TmdbApiKey) is { Length: > 0 } stored)
        {
            var own = Volatile.Read(ref _ownKey);
            if (own is null || !string.Equals(own.Stored, stored, StringComparison.Ordinal))
            {
                own = new OwnKey(stored, plugin.Secrets.Unprotect(stored).Trim());
                Volatile.Write(ref _ownKey, own);
            }

            if (own.Key.Length > 0)
            {
                return own.Key;
            }
        }

        if (Volatile.Read(ref _apiKey) is { } read && DateTime.UtcNow - read.AtUtc < TimeSpan.FromMinutes(1))
        {
            return read.Key;
        }

        var key = ReadApiKey();
        Volatile.Write(ref _apiKey, new ReadKey(DateTime.UtcNow, key));
        return key;
    }

    private sealed record ReadKey(DateTime AtUtc, string? Key);

    private sealed record OwnKey(string Stored, string Key);

    // The key Jellyfin asks TMDb with: the one set in its TMDb settings, else the one it ships with. Neither is part of
    // Jellyfin's API for plugins, so a Jellyfin that moves them leaves discovery without TMDb, said once in the log.
    private string? ReadApiKey()
    {
        try
        {
            var plugin = _pluginManager.Plugins.Select(p => p.Instance).FirstOrDefault(p => p?.GetType().FullName == TmdbPluginType) as IHasPluginConfiguration;
            if (plugin?.Configuration is { } config
                && JsonSerializer.SerializeToNode(config, config.GetType())?["TmdbApiKey"]?.GetValue<string>() is { Length: > 0 } own)
            {
                return own.Trim();
            }

            if (Type.GetType(TmdbUtilsType)?.GetField("ApiKey")?.GetRawConstantValue() is string { Length: > 0 } builtIn)
            {
                return builtIn;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or JsonException or TypeLoadException)
        {
            _logger.LogDebug(ex, "Reading Jellyfin's TMDb key failed");
        }

        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _logger.LogError("Jellyfin's TMDb key could not be found, so the request page's TMDb rows and browsing are off until the plugin is updated for this Jellyfin");
        }

        return null;
    }

    // TMDb's genre names by id for movies or shows, in the server's language: kept by the daily refresh.
    private async Task<IReadOnlyDictionary<int, string>> GenresAsync(RequestType type, CancellationToken cancellationToken, bool fresh = false)
    {
        var body = await GetAsync(type == RequestType.Movie ? "genre/movie/list" : "genre/tv/list", new Dictionary<string, string>(), cancellationToken, fresh).ConfigureAwait(false);
        return (body?["genres"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(g => Int(g["id"]) is > 0 && Text(g["name"]) is not null)
            .GroupBy(g => Int(g["id"])!.Value)
            .ToDictionary(g => g.Key, g => Text(g.First()["name"])!);
    }

    // ------------------------------------------------------------------ the home rows, kept ahead of time

    /// <summary>How many of TMDb's pages of each home row the daily refresh keeps.</summary>
    internal const int StoredPages = 5;

    // What a built-in home row's page is kept under: the row, the page, and the server's language and country.
    private string RowKey(string row, int page)
        => CacheKey("rows/" + row, new Dictionary<string, string> { ["page"] = page.ToString(CultureInfo.InvariantCulture), ["region"] = Locale().Region ?? string.Empty });

    // What says when the rows were last kept in full, in the server's language and country.
    private string RowsKey() => CacheKey("rows", new Dictionary<string, string> { ["region"] = Locale().Region ?? string.Empty });

    // A built-in home row's page as the daily refresh kept it, when it did.
    private JsonObject? RowPage(string? row, int page)
        => row is not null && page >= 1 && page <= StoredPages && Find(RowKey(row, page)) is { } kept ? JsonObject.Create(kept.Body) : null;

    /// <summary>
    /// Gets whether the home rows were kept in full within the last day, in the server's language, so nothing needs
    /// refreshing on startup.
    /// </summary>
    public bool StoreIsCurrent => Find(RowsKey()) is { } kept && DateTime.UtcNow - kept.SavedUtc < TimeSpan.FromDays(1);

    /// <summary>
    /// Reads the home rows' first pages, their shows (for their TVDB ids and season counts) and TMDb's genre names
    /// afresh from TMDb into the cache, then lets go of everything kept before: what the page was used for since the
    /// last refresh. Nothing is let go of if TMDb doesn't answer in full.
    /// </summary>
    /// <param name="rows">The home rows, by key: a built-in one names itself as its list's row; one of the server's own doesn't.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many titles were kept; 0 when TMDb didn't answer in full.</returns>
    public async Task<int> RefreshStoreAsync(IEnumerable<(string Row, TmdbList List)> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!Available)
        {
            return 0;
        }

        // One refresh at a time: the daily task and the one on startup can meet.
        await _storing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await StoreAsync(rows, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _storing.Release();
        }
    }

    private readonly SemaphoreSlim _storing = new(1, 1);

    /// <inheritdoc />
    public void Dispose()
    {
        _storing.Dispose();
        _inFlight.Dispose();
    }

    private async Task<int> StoreAsync(IEnumerable<(string Row, TmdbList List)> rows, CancellationToken cancellationToken)
    {
        // Everything read from here on is kept after this; a moment's room for a clock or a file time that rounds.
        var before = DateTime.UtcNow - TimeSpan.FromSeconds(5);
        foreach (var type in new[] { RequestType.Movie, RequestType.Series })
        {
            if ((await GenresAsync(type, cancellationToken, fresh: true).ConfigureAwait(false)).Count == 0)
            {
                return 0;
            }
        }

        var titles = 0;
        var kept = new List<(string Key, JsonObject Page)>();
        foreach (var (row, list) in rows)
        {
            var pages = new List<JsonObject>();
            var answered = true;
            for (var number = 1; number <= StoredPages; number++)
            {
                var parameters = new Dictionary<string, string>(list.Query, StringComparer.Ordinal) { ["page"] = number.ToString(CultureInfo.InvariantCulture) };
                if (await GetAsync(list.Path, parameters, cancellationToken, fresh: true).ConfigureAwait(false) is not { } body)
                {
                    // A row of the server's own TMDb won't list (what it picks by is gone, say) is left out, and read
                    // as it's shown; a built-in one leaves everything as it was.
                    if (list.Row is not null)
                    {
                        return 0;
                    }

                    answered = false;
                    break;
                }

                pages.Add(body);
                var results = (body["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
                if (list.Type == RequestType.Series)
                {
                    // Each show's own answer, which has its TVDB id (Sonarr and the library match shows by it) and how
                    // many seasons it has (whether one the library has still has some to request). One TMDb didn't
                    // answer for is asked about as it's shown.
                    await Task.WhenAll(results.Select(r => Int(r["id"])).OfType<int>().Select(async id =>
                    {
                        var tmdb = id.ToString(CultureInfo.InvariantCulture);
                        if (await TitleAsync(RequestType.Series, tmdb, cancellationToken, fresh: true).ConfigureAwait(false) is { } show)
                        {
                            NoteTvdbId(tmdb, show);
                        }
                    })).ConfigureAwait(false);
                }

                if (number >= Math.Min(Int(body["total_pages"]) ?? 1, LastPage))
                {
                    break;
                }
            }

            if (!answered)
            {
                continue;
            }

            titles += pages.Sum(page => (page["results"] as JsonArray)?.Count ?? 0);
            if (list.Row is not null)
            {
                kept.AddRange(pages.Select((page, i) => (RowKey(list.Row, i + 1), page)));
            }
        }

        // Answered in full: the built-in rows' pages are kept under the rows themselves, all at once, so a refresh that
        // stopped part way never leaves rows from two days; then everything kept before this refresh goes.
        foreach (var (key, page) in kept)
        {
            await KeepAsync(key, JsonSerializer.SerializeToUtf8Bytes(page)).ConfigureAwait(false);
        }

        await KeepAsync(RowsKey(), JsonSerializer.SerializeToUtf8Bytes(new JsonObject { ["titles"] = titles })).ConfigureAwait(false);
        var gone = Clear(before);
        _logger.LogInformation("Kept the request page's TMDb rows: {Titles} titles; let go of {Gone} answers kept before", titles, gone);
        return titles;
    }

    // A show's TVDB id from its own answer, kept for good once found.
    private string? NoteTvdbId(string tmdb, JsonObject show)
    {
        if (Int((show["external_ids"] as JsonObject)?["tvdb_id"]) is not (> 0 and var id))
        {
            return null;
        }

        var tvdb = id.ToString(CultureInfo.InvariantCulture);
        _tvdbIds[tmdb] = tvdb;
        return tvdb;
    }

    /// <summary>Gets a show's TVDB id when it's already known, without asking TMDb.</summary>
    /// <param name="tmdb">The show's TMDb id.</param>
    /// <returns>The TVDB id, or null.</returns>
    internal string? KnownTvdbId(string tmdb)
        => _tvdbIds.TryGetValue(tmdb, out var tvdb) ? tvdb
            // Or from the show's own answer, when that's kept: after a restart, the home rows' shows' are.
            : Find(CacheKey(TitlePath(RequestType.Series, tmdb), TitleQuery(RequestType.Series, credits: false))) is { } kept && JsonObject.Create(kept.Body) is { } show ? NoteTvdbId(tmdb, show)
            : null;

    /// <summary>Gets a show's TVDB id from TMDb, kept once found.</summary>
    /// <param name="tmdb">The show's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The TVDB id; null when TMDb has none yet or didn't answer.</returns>
    internal async Task<string?> TvdbIdAsync(string tmdb, CancellationToken cancellationToken)
    {
        if (KnownTvdbId(tmdb) is { } tvdb)
        {
            return tvdb;
        }

        // Until the next refresh at most: a new show's TVDB id is often added to TMDb some days after the show.
        var body = await GetAsync("tv/" + tmdb + "/external_ids", new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var found = Int(body["tvdb_id"]) is > 0 and var id ? id.ToString(CultureInfo.InvariantCulture) : null;
        // Shows' ids never change, but browsing reaches many: past a limit they're read again as needed.
        if (_tvdbIds.Count >= 20000)
        {
            _tvdbIds.Clear();
        }

        // Only an id found is kept for good; a show with none yet is asked about again.
        if (found is not null)
        {
            _tvdbIds[tmdb] = found;
        }

        return found;
    }

    /// <summary>
    /// Adds a show's TVDB id, which Sonarr knows it by, to its ids when they have only its TMDb id.
    /// </summary>
    /// <param name="ids">The show's ids, changed in place.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task FillTvdbAsync(Dictionary<string, string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (Available && !ids.ContainsKey(ProviderIds.Tvdb) && ids.TryGetValue(ProviderIds.Tmdb, out var tmdb)
            && await TvdbIdAsync(tmdb, cancellationToken).ConfigureAwait(false) is { } tvdb)
        {
            ids[ProviderIds.Tvdb] = tvdb;
        }
    }

    // What a request is kept under: its path and parameters, with the server's language (and, for discovering movies,
    // its country) added to the parameters.
    private string CacheKey(string path, Dictionary<string, string> query)
    {
        var (language, region) = Locale();
        if (language is not null)
        {
            query["language"] = language;
        }

        if (region is not null && path.StartsWith("discover/movie", StringComparison.Ordinal))
        {
            query["region"] = region;
        }

        return path + "?" + string.Join('&', query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)));
    }

    /// <summary>
    /// Gets the TMDb ids on a page of a list as it was last read (the answer kept), without asking
    /// TMDb: none when it hasn't been read.
    /// </summary>
    /// <param name="path">The list.</param>
    /// <param name="query">The list's own parameters.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="row">The home row the list is, if any.</param>
    /// <returns>The ids.</returns>
    public IEnumerable<string> KnownIds(string path, IReadOnlyDictionary<string, string> query, int page, string? row = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new Dictionary<string, string>(query, StringComparer.Ordinal) { ["page"] = page.ToString(CultureInfo.InvariantCulture) };
        var body = RowPage(row, page) ?? (Find(CacheKey(path, parameters)) is { } kept ? JsonObject.Create(kept.Body) : null);
        return (body?["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(r => Int(r["id"])?.ToString(CultureInfo.InvariantCulture)).OfType<string>();
    }

    // One TMDb answer, from the cache when it's kept there; fresh asks TMDb even so, and keeps the new answer. Null
    // when there's no key, or TMDb didn't answer.
    private async Task<JsonObject?> GetAsync(string path, Dictionary<string, string> query, CancellationToken cancellationToken, bool fresh = false)
    {
        var key = CacheKey(path, query);
        if (!fresh && Find(key) is { } kept)
        {
            return JsonObject.Create(kept.Body);
        }

        // Failed a moment ago, or TMDb asked for a pause: not asked again yet.
        if (Failing(key) || ApiKey() is not { } apiKey)
        {
            return null;
        }

        // A fetch keeps its turn even when whoever asked stops waiting, so past this many waiting no new question joins
        // them: one viewer asking faster than TMDb answers can't hold up everyone else's without end.
        if (_fetching.Count >= MaxWaiting && !_fetching.ContainsKey(key))
        {
            return null;
        }

        // One fetch however many callers ask at once; each stops waiting when it's cancelled.
        var fetching = _fetching.GetOrAdd(key, _ => new Lazy<Task<JsonElement?>>(() => FetchOnceAsync(key, path, apiKey)));
        return await fetching.Value.WaitAsync(cancellationToken).ConfigureAwait(false) is { } body ? JsonObject.Create(body) : null;
    }

    // Whether a question isn't to be asked of TMDb yet: it failed a moment ago, or TMDb asked for a pause.
    private bool Failing(string key)
    {
        var now = DateTime.UtcNow;
        if (now.Ticks < Interlocked.Read(ref _pausedUntilTicks))
        {
            return true;
        }

        if (_failed.TryGetValue(key, out var until))
        {
            if (now < until)
            {
                return true;
            }

            _failed.TryRemove(KeyValuePair.Create(key, until));
        }

        return false;
    }

    // Remembers that a question failed, so it isn't asked again until then.
    private void Failed(string key, TimeSpan forHowLong)
    {
        // Failures are brief, so those of many different questions are let go of once they've passed.
        if (_failed.Count >= 1000)
        {
            var now = DateTime.UtcNow;
            foreach (var passed in _failed.Where(f => f.Value <= now).ToList())
            {
                _failed.TryRemove(passed);
            }
        }

        _failed[key] = DateTime.UtcNow + forHowLong;
    }

    // Done, it's no longer being fetched, whoever stopped waiting for it, so the next ask reads the cache or fetches again.
    private async Task<JsonElement?> FetchOnceAsync(string key, string path, string apiKey)
    {
        try
        {
            return await FetchAsync(key, path, apiKey).ConfigureAwait(false);
        }
        finally
        {
            _fetching.TryRemove(key, out _);
        }
    }

    // How long to wait before the next question may start, taking its place: none while the bucket holds a question's
    // worth, otherwise until the rate has refilled it. Places are taken in turn, so a burst is spread out over time.
    private TimeSpan NextSlot()
    {
        lock (_bucket)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            _tokens = Math.Min(Burst, _tokens + (System.Diagnostics.Stopwatch.GetElapsedTime(_filledAt, now).TotalSeconds * RequestsPerSecond));
            _filledAt = now;
            _tokens -= 1;
            return _tokens >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-_tokens / RequestsPerSecond);
        }
    }

    // Fetches one TMDb answer and keeps it; at most 15 seconds once asked, whoever is waiting. Waits its
    // turn first, among every question being asked of TMDb.
    private async Task<JsonElement?> FetchAsync(string key, string path, string apiKey)
    {
        try
        {
            await _inFlight.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
            return null;
        }

        try
        {
            var wait = NextSlot();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
            }

            // A pause TMDb asked for while this waited its turn.
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _pausedUntilTicks))
            {
                return null;
            }

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            // Over HTTP/2 where TMDb offers it, so the questions asked together share one connection rather than opening
            // one each.
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase + key + "&api_key=" + Uri.EscapeDataString(apiKey)))
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            using var response = await client.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // TMDb has no such title (any more): an answer, kept as nothing, so it isn't asked again and again.
                return await KeepAsync(key, "{}"u8.ToArray()).ConfigureAwait(false);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Asked too fast: nothing more is asked for as long as TMDb says, within reason.
                var retry = response.Headers.RetryAfter is { } after
                    ? after.Delta ?? (after.Date is { } date ? date - DateTimeOffset.UtcNow : DefaultPause)
                    : DefaultPause;
                var pause = retry < TimeSpan.Zero ? TimeSpan.Zero : retry > LongestPause ? LongestPause : retry;
                Interlocked.Exchange(ref _pausedUntilTicks, (DateTime.UtcNow + pause).Ticks);
                Failed(key, pause > FailedFor ? pause : FailedFor);
                _logger.LogDebug("TMDb asked for a pause of {Seconds} seconds at {Path}", (int)pause.TotalSeconds, path);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500)
                {
                    Failed(key, FailedFor);
                }
                else if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // A key TMDb refuses: said once where an administrator sees it, and not asked again at once.
                    Failed(key, RefusedFor);
                    if (Interlocked.Exchange(ref _warnedRefused, 1) == 0)
                    {
                        _logger.LogError("TMDb refused the API key ({Status}), so the request page's TMDb rows and details are empty; check the TMDb API key in the Requests settings", (int)response.StatusCode);
                    }
                }

                _logger.LogDebug("TMDb answered {Status} for {Path}", (int)response.StatusCode, path);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return await KeepAsync(key, bytes).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A timeout included: whoever was waiting has been told by now if they gave up.
            _logger.LogDebug(ex, "TMDb did not answer for {Path}", path);
            Failed(key, FailedFor);
            return null;
        }
        finally
        {
            try
            {
                _inFlight.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutting down: a question still being asked outlived the client.
            }
        }
    }

    // An answer kept: since when, the answer, and about how many bytes it takes. A class, so the cache's compare-and-swap
    // compares answers by which one they are.
    private sealed class KeptAnswer(DateTime savedUtc, JsonElement body, long bytes)
    {
        public DateTime SavedUtc { get; } = savedUtc;

        public JsonElement Body { get; } = body;

        public long Bytes { get; } = bytes;
    }

    // An answer as it's kept, from memory, else from the cache folder (and then in memory again); null when it isn't
    // kept, or was kept too long ago to use.
    private KeptAnswer? Find(string key)
    {
        if (_cache.TryGetValue(key, out var kept))
        {
            return DateTime.UtcNow - kept.SavedUtc < KeptFor ? kept : null;
        }

        return ReadKept(key) is { } read ? Remember(key, read.Body, read.Bytes, read.SavedUtc) : null;
    }

    // Keeps an answer, in memory and in the cache folder; null when it isn't JSON's object.
    private async Task<JsonElement?> KeepAsync(string key, byte[] bytes)
    {
        JsonElement body;
        using (var document = JsonDocument.Parse(bytes))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // A copy of its own, as the document lends its memory back once disposed.
            body = document.RootElement.Clone();
        }

        Remember(key, body, bytes.Length, DateTime.UtcNow);
        await WriteKeptAsync(key, bytes).ConfigureAwait(false);
        return body;
    }

    // Keeps an answer in memory, making room first: those let go of there are still in the cache folder.
    private KeptAnswer Remember(string key, JsonElement body, long bytes, DateTime savedUtc)
    {
        var kept = new KeptAnswer(savedUtc, body, bytes);
        while (true)
        {
            if (_cache.TryGetValue(key, out var before))
            {
                if (_cache.TryUpdate(key, kept, before))
                {
                    Interlocked.Add(ref _keptBytes, bytes - before.Bytes);
                    break;
                }
            }
            else if (_cache.TryAdd(key, kept))
            {
                Interlocked.Add(ref _keptBytes, bytes);
                break;
            }
        }

        if (Interlocked.Read(ref _keptBytes) > MaxKeptBytes)
        {
            Trim();
        }

        return kept;
    }

    // Lets answers go from memory until they take three quarters of what may be kept there, the longest kept first.
    // One trim at a time; any other fetch finding too much kept leaves it to that one.
    private void Trim()
    {
        if (Interlocked.Exchange(ref _trimming, 1) == 1)
        {
            return;
        }

        try
        {
            var target = MaxKeptBytes * 3 / 4;
            foreach (var answer in _cache.OrderBy(c => c.Value.SavedUtc).ToList())
            {
                if (Interlocked.Read(ref _keptBytes) <= target)
                {
                    break;
                }

                if (_cache.TryRemove(answer))
                {
                    Interlocked.Add(ref _keptBytes, -answer.Value.Bytes);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }

    /// <summary>
    /// Lets go of every answer kept before a time, in memory and in the cache folder: what a refresh didn't just read.
    /// </summary>
    /// <param name="beforeUtc">The time.</param>
    /// <returns>How many answers went.</returns>
    internal int Clear(DateTime beforeUtc)
    {
        foreach (var answer in _cache.Where(c => c.Value.SavedUtc < beforeUtc).ToList())
        {
            if (_cache.TryRemove(answer))
            {
                Interlocked.Add(ref _keptBytes, -answer.Value.Bytes);
            }
        }

        return ClearKept(beforeUtc);
    }

    /// <summary>Gets about how many bytes of TMDb's answers are kept in memory.</summary>
    internal long KeptBytes => Interlocked.Read(ref _keptBytes);

    private static string? Image(JsonNode? path, string size)
        => Text(path) is { Length: > 1 } p && p[0] == '/' ? ImageBase + size + p : null;

    private static string? Text(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { Length: > 0 } s ? s.Trim() : null;

    private static int? Int(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var n) ? n : null;
}

/// <summary>A page of a TMDb list: its titles, how many the list holds, and how many pages.</summary>
/// <param name="Items">The page's titles.</param>
/// <param name="TotalResults">How many titles the list holds.</param>
/// <param name="TotalPages">How many pages it has, at most TMDb's last.</param>
public sealed record TmdbPage(List<SearchResult> Items, int TotalResults, int TotalPages);

/// <summary>A title's page details as TMDb gives them.</summary>
/// <param name="Name">The title.</param>
/// <param name="Tagline">Its tagline.</param>
/// <param name="Cast">Its cast, in billing order.</param>
/// <param name="Directors">Its directors.</param>
/// <param name="Writers">Its writers.</param>
/// <param name="Studios">Its studios, or a show's networks.</param>
/// <param name="Genres">Its genres.</param>
/// <param name="Certification">Its age rating in the server's country.</param>
/// <param name="ReleaseDate">When it came out, or a show first aired.</param>
/// <param name="Rating">TMDb's rating, out of ten.</param>
/// <param name="EndYear">An ended show's last year.</param>
public sealed record TmdbTitle(string? Name, string? Tagline, List<TmdbCredit> Cast, List<TmdbCredit> Directors, List<TmdbCredit> Writers, List<string> Studios, List<string> Genres, string? Certification, DateTime? ReleaseDate, double? Rating, int? EndYear)
{
    /// <summary>Gets its trailer on YouTube, when TMDb lists one.</summary>
    public string? TrailerUrl { get; init; }

    /// <summary>Gets the TMDb collection a movie belongs to, such as Dune Collection, with its id.</summary>
    public BrowseLink? Collection { get; init; }

    /// <summary>Gets its keywords, original language and collection, for the blocklist.</summary>
    public TitleTraits? Traits { get; init; }
}

/// <summary>A TMDb collection: a set of movies, such as Dune Collection.</summary>
/// <param name="Id">Its TMDb id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Overview">What it is.</param>
/// <param name="PosterUrl">Its poster.</param>
/// <param name="BackdropUrl">Its backdrop.</param>
/// <param name="Parts">Its movies, in release order.</param>
public sealed record TmdbMovieSet(int Id, string Name, string? Overview, string? PosterUrl, string? BackdropUrl, List<SearchResult> Parts);

/// <summary>A cast or crew member as TMDb gives them.</summary>
/// <param name="Name">Their name.</param>
/// <param name="Role">The character played, or the job.</param>
/// <param name="ImageUrl">Their photo, when TMDb has one.</param>
/// <param name="TmdbId">Their TMDb id.</param>
public sealed record TmdbCredit(string Name, string? Role, string? ImageUrl, string? TmdbId);

/// <summary>Something TMDb lists by id, such as a genre, keyword, studio or streaming service.</summary>
/// <param name="Id">Its TMDb id.</param>
/// <param name="Name">Its name.</param>
/// <param name="LogoUrl">Its logo, when it has one.</param>
public sealed record TmdbChoice(int Id, string Name, string? LogoUrl);

/// <summary>A person, from TMDb.</summary>
/// <param name="Name">Their name.</param>
/// <param name="Overview">Their biography.</param>
/// <param name="BirthDate">When they were born.</param>
/// <param name="DeathDate">When they died.</param>
/// <param name="Birthplace">Where they were born.</param>
/// <param name="ImageUrl">Their photo.</param>
public sealed record TmdbPerson(string Name, string? Overview, DateTime? BirthDate, DateTime? DeathDate, string? Birthplace, string? ImageUrl)
{
    /// <summary>Gets the movies and shows they're in or worked on, most popular first.</summary>
    public List<SearchResult> Credits { get; init; } = new();
}

/// <summary>A title as a request records it, from TMDb.</summary>
/// <param name="Title">Its name.</param>
/// <param name="Year">Its year.</param>
/// <param name="Overview">Its overview.</param>
/// <param name="PosterUrl">Its poster.</param>
/// <param name="BackdropUrl">Its backdrop.</param>
/// <param name="ExpectedEpisodes">For a show, the episodes each season has aired.</param>
public sealed record TmdbDescription(string Title, int? Year, string? Overview, string? PosterUrl, string? BackdropUrl, Dictionary<int, int>? ExpectedEpisodes)
{
    /// <summary>Gets the genres, as TMDb names them.</summary>
    public List<string> Genres { get; init; } = new();

    /// <summary>Gets the age rating in the server's country, when known.</summary>
    public string? Certification { get; init; }

    /// <summary>Gets whether it is out, or null when unknown.</summary>
    public bool? Released { get; init; }

    /// <summary>Gets every id the title is known by.</summary>
    public Dictionary<string, string> ProviderIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets its keywords, original language and collection, for the blocklist.</summary>
    public TitleTraits? Traits { get; init; }
}
