using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Talks to Radarr (movies) and Sonarr (shows) through their v3 APIs: adding approved requests, listing a
/// show's seasons, and reading back how a download is going. Transient failures are retried, and a server
/// that keeps failing is left alone for a while so the status sync does not hammer it.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public class ArrClient : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ArrClient> _logger;
    private readonly MediaBrowser.Common.Configuration.IApplicationPaths? _paths;
    // One breaker per server, so one that is down doesn't stop requests to the others. Lookups requesters drive
    // (searching, a show's seasons, checking a title as it's requested) have breakers of their own: anyone signed in
    // can make those fail, and that must never stop approvals being sent or the status sync for a while.
    private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CircuitBreaker> _lookupBreakers = new(StringComparer.Ordinal);
    // Marking lists from elsewhere asks about many titles anyone can pick, so its failures have breakers of their own:
    // they must never stop a request being checked as it's made.
    private readonly ConcurrentDictionary<string, CircuitBreaker> _markingBreakers = new(StringComparer.Ordinal);
    // Whether Radarr or Sonarr has a title and its files, by type and id, as last asked, for marking lists from elsewhere;
    // and the questions being asked now, so lists marked at once share them.
    private readonly ConcurrentDictionary<string, (DateTime AtUtc, bool Has, bool Files, bool Answered)> _held = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<(bool Has, bool Files)>>> _asking = new(StringComparer.Ordinal);
    // How many titles are asked about at once, by every list being marked together, so busy pages don't flood the servers.
    private readonly SemaphoreSlim _heldGate = new(HeldLookups);

    /// <summary>How long whether Radarr or Sonarr has a title is kept, for marking lists from elsewhere.</summary>
    internal static readonly TimeSpan OwnedFor = TimeSpan.FromMinutes(10);

    // How long a server that didn't answer whether it has a title is left before it's asked again.
    private static readonly TimeSpan UnansweredFor = TimeSpan.FromMinutes(1);

    // How many titles are asked about at once when marking lists.
    private const int HeldLookups = 8;

    // How many answers are kept before the stale ones are let go.
    private const int HeldKept = 5000;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="paths">The server's folders, for the cache folder the libraries read are kept in; none keeps them in memory only.</param>
    public ArrClient(IHttpClientFactory httpClientFactory, ILogger<ArrClient> logger, MediaBrowser.Common.Configuration.IApplicationPaths? paths = null)
        : this(logger, paths, new NoRedirectHttpClientFactory(httpClientFactory))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArrClient"/> class that sends every call through the factory given.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="paths">The server's folders, for the cache folder the libraries read are kept in.</param>
    /// <param name="sender">Sends the calls, such as a test's stand-in for Radarr and Sonarr.</param>
    internal ArrClient(ILogger<ArrClient> logger, MediaBrowser.Common.Configuration.IApplicationPaths? paths, IHttpClientFactory sender)
    {
        _httpClientFactory = sender;
        _logger = logger;
        _paths = paths;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the lookup gate and the lock saving the libraries.</summary>
    /// <param name="disposing">Whether it's being disposed rather than finalized.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _heldGate.Dispose();
            _saving.Dispose();
        }
    }

    /// <summary>
    /// Returns whether a server with a profile is set up for this type, so approving a request should send it on.
    /// </summary>
    /// <param name="type">The request type.</param>
    /// <returns>True when a Radarr or Sonarr server is set up for it.</returns>
    public static bool IsConfigured(RequestType type) => Target.For(type) is not null;

    /// <summary>
    /// Returns the name of the server a request goes to, such as Radarr or 4K Radarr.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The server's name, or Radarr or Sonarr when none is set up.</returns>
    public static string NameFor(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Target.For(request)?.Name ?? NameFor(request.Type);
    }

    /// <summary>Gets whether two server addresses are the same once normalized, so a saved key stays with its address.</summary>
    /// <param name="a">One address.</param>
    /// <param name="b">The other.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool SameAddress(string? a, string? b)
        => TryNormalizeUrl(a, out var x) && TryNormalizeUrl(b, out var y) && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the name of the server requests of this type go to.
    /// </summary>
    /// <param name="type">The request type.</param>
    /// <returns>Radarr or Sonarr.</returns>
    public static string NameFor(RequestType type) => type == RequestType.Movie ? "Radarr" : "Sonarr";

    /// <summary>Gets the server and profile a request goes to now, as its sends and status reads work it out.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The server's and profile's ids, or nulls when none is set up for it.</returns>
    public static (string? ServerId, string? ProfileId) PlacementFor(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Target.For(request) is { } target ? (target.ServerId, target.ProfileId) : (null, null);
    }

    /// <summary>
    /// Adds a request's title to Radarr or Sonarr with its profile's settings, and starts a search when its server
    /// searches on add. A title already there is monitored (for a show, the requested seasons), given the profile's
    /// tags, and searched for again the same way.
    /// </summary>
    /// <param name="request">The approved request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The id Radarr or Sonarr knows the title by, or what went wrong in words for the administrator.</returns>
    public async Task<(int? ArrId, string? Error)> AddAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = Target.For(request);
        var sent = await AddToAsync(target, request, cancellationToken).ConfigureAwait(false);

        // What's sent is in that server's library from now on, without waiting for its next read, and an answer kept
        // from before it was sent is let go.
        if (target is not null && sent.ArrId is not null
            && request.ProviderIds.GetValueOrDefault(request.Type == RequestType.Movie ? ProviderIds.Tmdb : ProviderIds.Tvdb) is { } id)
        {
            _held.TryRemove(LibraryKey(target, request.Type) + ":" + id, out _);
            if (_libraries.TryGetValue(LibraryKey(target, request.Type), out var library) && !library.Files.ContainsKey(id))
            {
                _libraries.TryUpdate(LibraryKey(target, request.Type), library with { Files = new Dictionary<string, bool>(library.Files, StringComparer.Ordinal) { [id] = false } }, library);
            }
        }

        return sent;
    }

    private async Task<(int? ArrId, string? Error)> AddToAsync(Target? target, MediaRequest request, CancellationToken cancellationToken)
    {
        if (target is null)
        {
            // None set up: approved without being sent. One set up that this request's server can't be reached
            // through for now (its key being re-entered, say) is a failed send, retried, not a sent one.
            var anySetUp = RequestConfig.Instance?.ReadConfiguration(c => (c.ArrServers ?? new()).Any(s => s?.Type == request.Type)) == true;
            return anySetUp ? (null, ServerWording.Format(TranslationKeys.RequestsErrorArrUnavailable, NameFor(request))) : (null, null);
        }

        var lookup = request.Type == RequestType.Movie
            ? await LookUpMovieAsync(target, request, cancellationToken).ConfigureAwait(false)
            : await LookUpSeriesAsync(target, request.Title, request.Year, request.ProviderIds, cancellationToken).ConfigureAwait(false);
        if (lookup.Error is not null || lookup.Item is null)
        {
            return (null, lookup.Error ?? NotFound(target, request.Type));
        }

        var item = lookup.Item;
        var existingId = Int(item["id"]);
        if (existingId > 0)
        {
            return await SearchExistingAsync(target, request, item, existingId.Value, cancellationToken).ConfigureAwait(false);
        }

        var added = await AddNewAsync(target, request, item, cancellationToken).ConfigureAwait(false);
        if (added.Error is null)
        {
            // A show added for its specials is added with nothing monitored; they're then switched on one by one.
            return request.Specials is not null && added.ArrId is { } newId
                ? await SendSpecialsAsync(target, request, newId, justAdded: true, cancellationToken).ConfigureAwait(false)
                : added;
        }

        // Adding is not retried, since a timed-out add may still have gone through; look again before calling it
        // a failure. A title that is there now (added by that attempt, or by someone else meanwhile) is a success.
        var again = request.Type == RequestType.Movie
            ? await LookUpMovieAsync(target, request, cancellationToken).ConfigureAwait(false)
            : await LookUpSeriesAsync(target, request.Title, request.Year, request.ProviderIds, cancellationToken).ConfigureAwait(false);
        return Int(again.Item?["id"]) is > 0 and var id
            ? await SearchExistingAsync(target, request, again.Item!, id, cancellationToken).ConfigureAwait(false)
            : added;
    }

    // Each server's whole library as last read: every title's id (TMDb for movies, TVDB for shows) and whether it has
    // files, by server and type.
    private readonly ConcurrentDictionary<string, ArrLibrary> _libraries = new(StringComparer.Ordinal);

    /// <summary>How long a server's whole library, as read, answers whether it has a title as a request is checked.</summary>
    internal static readonly TimeSpan LibraryFor = TimeSpan.FromMinutes(20);

    /// <summary>
    /// How long a server's whole library, as read, marks lists (the home rows, search, More Like This): a day between
    /// the daily task's reads, with room for one that runs late. Read again as soon as the page is used (see
    /// DiscoveryWarmer), so an older copy only marks what's shown until that read is done, and nobody waits on the
    /// server for it.
    /// </summary>
    internal static readonly TimeSpan MarksFor = TimeSpan.FromDays(2);

    // The largest library read: well past any real one, short of a server sending without end.
    private const long MaxLibraryBytes = 512L * 1024 * 1024;

    // A server's library as read: when, each title's files, and whether its shows were kept by their TMDb ids too (Sonarr
    // from version 4 lists them; version 3 doesn't).
    private sealed record ArrLibrary(DateTime AtUtc, Dictionary<string, bool> Files, bool ByTmdb = true);

    private static string LibraryKey(Target target, RequestType type) => type + "|" + (target.ServerId ?? target.Url);

    /// <summary>
    /// Reads every Radarr's and Sonarr's whole library (their movie and series lists), keeping only each title's id and
    /// whether it has files, so lists, search and More Like This can say which titles they have without asking about each
    /// one. Read as it arrives, title by title, however large the library. A server that doesn't answer keeps what was
    /// read before, and titles are asked about one at a time once that's old. What was read is kept in the cache folder
    /// too, so it's there after a restart.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RefreshLibrariesAsync(CancellationToken cancellationToken)
    {
        var read = await ReadLibrariesAsync(cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            await SaveLibrariesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lets go of what each server said about single titles (asked as lists were marked without its library read):
    /// the daily refresh has just read their whole libraries.
    /// </summary>
    public void ClearHeld() => _held.Clear();

    // Reads each server's library; how many answered.
    private async Task<int> ReadLibrariesAsync(CancellationToken cancellationToken)
    {
        var answered = 0;
        foreach (var type in new[] { RequestType.Movie, RequestType.Series })
        {
            foreach (var target in Target.AllFor(type))
            {
                try
                {
                    if (await ReadLibraryAsync(target, type, cancellationToken).ConfigureAwait(false) is { } read)
                    {
                        _libraries[LibraryKey(target, type)] = new ArrLibrary(DateTime.UtcNow, read.Files, read.ByTmdb);
                        answered++;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    _logger.LogDebug(ex, "Reading {Server}'s library failed", target.Name);
                }
            }
        }

        return answered;
    }

    // Where the libraries read are kept: Jellyfin's cache folder, as they're Radarr's and Sonarr's to read again
    // whenever they're lost. Only ids and whether each has files: no address and no key.
    private string? LibrariesPath => _paths?.CachePath is { Length: > 0 } cache ? Path.Combine(cache, "requests", "arr-libraries.json") : null;

    private readonly SemaphoreSlim _saving = new(1, 1);

    /// <summary>
    /// Gets a value indicating whether every server set up has had its whole library read within the last day, so
    /// nothing needs reading on startup.
    /// </summary>
    internal bool LibrariesAreCurrent
        => new[] { RequestType.Movie, RequestType.Series }.All(type => Target.AllFor(type).All(target =>
            _libraries.TryGetValue(LibraryKey(target, type), out var read) && DateTime.UtcNow - read.AtUtc < TimeSpan.FromDays(1)));

    /// <summary>
    /// Reads the libraries kept in the cache folder, if any, so lists are marked from them from the first visit after
    /// a restart. One read since (the server started faster than this) is left as it is.
    /// </summary>
    public void LoadLibraries()
    {
        if (LibrariesPath is not { } path)
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var file = File.OpenRead(path);
            foreach (var (key, kept) in JsonSerializer.Deserialize<Dictionary<string, KeptLibrary>>(file) ?? new())
            {
                if (kept?.Files is not null)
                {
                    _libraries.TryAdd(key, new ArrLibrary(kept.AtUtc, new Dictionary<string, bool>(kept.Files, StringComparer.Ordinal), kept.ByTmdb));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "The kept copy of Radarr's and Sonarr's libraries could not be read; they are read from the servers instead");
        }
    }

    // Writes the libraries read whole and moves them into place, so a copy half written is never read. Those of servers
    // no longer set up are left out. A copy that can't be written is still used until the server restarts.
    private async Task SaveLibrariesAsync(CancellationToken cancellationToken)
    {
        if (LibrariesPath is not { } path)
        {
            return;
        }

        var keys = new[] { RequestType.Movie, RequestType.Series }
            .SelectMany(type => Target.AllFor(type).Where(target => target.ServerId is not null).Select(target => LibraryKey(target, type)))
            .ToHashSet(StringComparer.Ordinal);
        var kept = _libraries.Where(l => keys.Contains(l.Key))
            .ToDictionary(l => l.Key, l => new KeptLibrary { AtUtc = l.Value.AtUtc, Files = l.Value.Files, ByTmdb = l.Value.ByTmdb }, StringComparer.Ordinal);
        await _saving.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicFile.WriteAsync(
                path,
                (file, ct) => JsonSerializer.SerializeAsync(file, kept, cancellationToken: ct),
                cancellationToken,
                flushToDisk: false,
                logger: _logger).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Radarr's and Sonarr's libraries could not be written to the cache folder; they are used until the server restarts");
        }
        finally
        {
            _saving.Release();
        }
    }

    // A server's library as kept in the cache folder.
    private sealed class KeptLibrary
    {
        public DateTime AtUtc { get; set; }

        public Dictionary<string, bool>? Files { get; set; }

        public bool ByTmdb { get; set; } = true;
    }

    private async Task<(Dictionary<string, bool> Files, bool ByTmdb)?> ReadLibraryAsync(Target target, RequestType type, CancellationToken cancellationToken)
    {
        // Three minutes for all of it: the client's timeout ends once the headers arrive, and a server that stalls
        // partway through mustn't hold up every later refresh.
        using var client = _httpClientFactory.CreateClient(nameof(ArrClient));
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        cancellationToken = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, target.Url + (type == RequestType.Movie ? "/api/v3/movie" : "/api/v3/series"));
        request.Headers.Add("X-Api-Key", target.ApiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxLibraryBytes)
        {
            _logger.LogDebug("{Server} answered {Status} for its library", target.Name, (int)response.StatusCode);
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var files = new Dictionary<string, bool>(StringComparer.Ordinal);
            bool anyShow = false, anyByTmdb = false;
            await foreach (var title in JsonSerializer.DeserializeAsyncEnumerable<LibraryTitle>(stream, LibraryJson, cancellationToken).ConfigureAwait(false))
            {
                var id = type == RequestType.Movie ? title?.TmdbId : title?.TvdbId;
                if (title is null || id is not > 0)
                {
                    continue;
                }

                var key = id.Value.ToString(CultureInfo.InvariantCulture);
                var hasFiles = type == RequestType.Movie ? title.HasFile == true : (title.Statistics?.EpisodeFileCount ?? 0) > 0;
                files[key] = files.GetValueOrDefault(key) || hasFiles;

                // A show is also kept by its TMDb id, for one listed before its TVDB id is known.
                anyShow |= type == RequestType.Series;
                if (type == RequestType.Series && title.TmdbId is > 0)
                {
                    anyByTmdb = true;
                    var byTmdb = TmdbKey(title.TmdbId.Value.ToString(CultureInfo.InvariantCulture));
                    files[byTmdb] = files.GetValueOrDefault(byTmdb) || hasFiles;
                }
            }

            // An empty library answers for every show, whatever it's known by.
            return (files, !anyShow || anyByTmdb);
        }
    }

    /// <summary>
    /// Gets whether every Radarr (movies) or Sonarr (shows) set up can say whether it has a title by its TMDb id alone,
    /// from its whole library read lately: always for Radarr, and for a Sonarr whose library lists its shows' TMDb ids.
    /// Otherwise a show is asked about by its TVDB id, which lists from TMDb don't carry until it's looked up.
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <returns>True when TMDb ids are enough.</returns>
    internal bool KnowsByTmdb(RequestType type)
        => type == RequestType.Movie || Target.AllFor(type).All(target => Library(target, type, MarksFor) is { ByTmdb: true });

    private static readonly JsonSerializerOptions LibraryJson = new() { PropertyNameCaseInsensitive = true };

    // The little of a library's title that's kept.
    private sealed class LibraryTitle
    {
        public int? TmdbId { get; set; }

        public int? TvdbId { get; set; }

        public bool? HasFile { get; set; }

        public LibraryStatistics? Statistics { get; set; }
    }

    private sealed class LibraryStatistics
    {
        public int EpisodeFileCount { get; set; }
    }

    /// <summary>
    /// Gets which of some titles any Radarr (movies) or Sonarr (shows) set up already has, by TMDb id for movies and TVDB
    /// id for shows, and which of those it has files for: so a list from elsewhere marks them as search does. Each server
    /// whose whole library was read lately answers from that; any other is asked about each title on its own (Radarr's
    /// movie?tmdbId=, Sonarr's series?tvdbId=), a few at a time. Each server's answer is kept for a few minutes, and one
    /// a server didn't give for a minute, so a server that's down isn't asked again on every page.
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="ids">The titles' TMDb ids (movies) or TVDB ids (shows).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="fresh">Asked again even when a kept answer is still fresh, to keep the answers warm.</param>
    /// <param name="live">Each server is asked, even one whose library was read lately.</param>
    /// <returns>Those it has, and those with files.</returns>
    public async Task<ArrHeld> HeldAsync(RequestType type, IEnumerable<string> ids, CancellationToken cancellationToken, bool fresh = false, bool live = false)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var owned = new ArrHeld(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var wanted = ids.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        var targets = Target.AllFor(type);
        if (targets.Count == 0 || wanted.Count == 0)
        {
            return owned;
        }

        void Mark(string id, bool has, bool files)
        {
            if (has)
            {
                owned.Ids.Add(id);
            }

            if (files)
            {
                owned.WithFiles.Add(id);
            }
        }

        // Each server's whole library read lately (RefreshLibrariesAsync) answers for it with no question asked.
        var asked = new List<Target>();
        foreach (var target in targets)
        {
            if (live || Library(target, type, MarksFor) is not { } library)
            {
                asked.Add(target);
                continue;
            }

            foreach (var id in wanted)
            {
                if (library.Files.TryGetValue(id, out var withFiles))
                {
                    Mark(id, true, withFiles);
                }
            }
        }

        if (asked.Count == 0)
        {
            return owned;
        }

        // Titles already known to have files need nothing more from the others. A show known only by its TMDb id can't
        // be asked about on its own.
        var answers = await Task.WhenAll(wanted.Where(id => !owned.WithFiles.Contains(id) && !id.StartsWith(TmdbPrefix, StringComparison.Ordinal)).SelectMany(id => asked.Select(async target =>
        {
            var key = LibraryKey(target, type) + ":" + id;
            if (!fresh && _held.TryGetValue(key, out var kept) && DateTime.UtcNow - kept.AtUtc < (kept.Answered ? OwnedFor : UnansweredFor))
            {
                return (Id: id, kept.Has, kept.Files);
            }

            // One question per server and title however many lists want it; each caller stops waiting when it's cancelled.
            var asking = _asking.GetOrAdd(key, _ => new Lazy<Task<(bool Has, bool Files)>>(() => AskHeldAsync(key, type, id, target)));
            var (has, files) = await asking.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            return (Id: id, Has: has, Files: files);
        }))).ConfigureAwait(false);

        foreach (var (id, has, files) in answers)
        {
            Mark(id, has, files);
        }

        return owned;
    }

    private const string TmdbPrefix = "tmdb:";

    /// <summary>Gets the id a show is kept under by its TMDb id, for one whose TVDB id isn't known.</summary>
    /// <param name="tmdb">The show's TMDb id.</param>
    /// <returns>The id.</returns>
    internal static string TmdbKey(string tmdb) => TmdbPrefix + tmdb;

    // A server's whole library, when it was read lately enough to answer from: within LibraryFor for a request being
    // checked, and within MarksFor for marking lists, which are read again as the page is used.
    private ArrLibrary? Library(Target target, RequestType type, TimeSpan? within = null)
        => _libraries.TryGetValue(LibraryKey(target, type), out var read) && DateTime.UtcNow - read.AtUtc < (within ?? LibraryFor) ? read : null;

    // Asks one server whether it has a title, and its files, and keeps the answer, or that it gave none. Asked at most
    // HeldLookups at a time across every list being marked. Done, it's no longer being asked, whoever stopped waiting
    // for it, so the next question asks again rather than reusing this answer.
    private async Task<(bool Has, bool Files)> AskHeldAsync(string key, RequestType type, string id, Target target)
    {
        try
        {
            await _heldGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
            _asking.TryRemove(key, out _);
            return (false, false);
        }

        try
        {
            bool has = false, files = false;
            var path = type == RequestType.Movie ? "/api/v3/movie?tmdbId=" : "/api/v3/series?tvdbId=";
            var result = await SendAsync(target, HttpMethod.Get, path + Uri.EscapeDataString(id), CancellationToken.None, forRequester: true, forMarking: true).ConfigureAwait(false);
            foreach (var title in result.Ok ? (Parse(result.Body) as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>() : Enumerable.Empty<JsonObject>())
            {
                has = true;
                files |= type == RequestType.Movie ? title["hasFile"]?.GetValueKind() == JsonValueKind.True : (Int(title["statistics"]?["episodeFileCount"]) ?? 0) > 0;
            }

            if (_held.Count >= HeldKept)
            {
                // The stale go, and if that isn't enough, the oldest.
                var now = DateTime.UtcNow;
                var gone = _held.Where(h => now - h.Value.AtUtc >= OwnedFor).Select(h => h.Key).ToList();
                if (_held.Count - gone.Count >= HeldKept)
                {
                    gone.AddRange(_held.OrderBy(h => h.Value.AtUtc).Take(HeldKept / 5).Select(h => h.Key));
                }

                foreach (var stale in gone)
                {
                    _held.TryRemove(stale, out _);
                }
            }

            _held[key] = (DateTime.UtcNow, has, files, result.Ok);
            return (has, files);
        }
        finally
        {
            _asking.TryRemove(key, out _);
            try
            {
                _heldGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Shutting down: a question still being asked outlived the client.
            }
        }
    }

    /// <summary>
    /// Gets whether any Radarr (for a movie) or Sonarr (for a show) set up already looks after the title these ids name,
    /// asking every server of the type: a show without a TVDb id is found by its title and its other ids. One it holds
    /// but has let go of (a movie unmonitored without its file, seasons of a show it doesn't monitor) doesn't count, so
    /// it can be requested again and the request switches them back on.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="title">The title, for finding a show without a TVDb id.</param>
    /// <param name="year">The year, for the same.</param>
    /// <param name="ids">The provider ids.</param>
    /// <param name="seasons">The seasons of a show asked for; null for all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when one looks after it, false when every one answered without it (or none is set up), null when one couldn't be asked.</returns>
    public async Task<bool?> HasAsync(RequestType type, string title, int? year, Dictionary<string, string> ids, IReadOnlyCollection<int>? seasons, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Every server at once. One whose whole library was read lately and has the title says so from it; otherwise a
        // title with the id the server keeps it by is asked about in its own library, which is quick, and only one
        // without is looked up by its other ids or title.
        var key = ids.GetValueOrDefault(type == RequestType.Movie ? ProviderIds.Tmdb : ProviderIds.Tvdb);
        var answers = await Task.WhenAll(Target.AllFor(type).Select(async target =>
        {
            // The whole library only says whether there are files, which settles a movie; anything else is asked.
            if (type == RequestType.Movie && key is not null && Library(target, type) is { } library && library.Files.GetValueOrDefault(key))
            {
                return (bool?)true;
            }

            try
            {
                if (key is not null)
                {
                    var path = type == RequestType.Movie ? "/api/v3/movie?tmdbId=" : "/api/v3/series?tvdbId=";
                    var held = await SendAsync(target, HttpMethod.Get, path + Uri.EscapeDataString(key), cancellationToken, retries: 1, forRequester: true).ConfigureAwait(false);
                    return held.Ok ? (Parse(held.Body) as JsonArray)?.OfType<JsonObject>().Any(t => LooksAfter(t, type, seasons)) == true : null;
                }

                var (item, error) = type == RequestType.Movie
                    ? await LookUpMovieAsync(target, new MediaRequest { Type = type, Title = title, Year = year, ProviderIds = ids }, cancellationToken, forRequester: true).ConfigureAwait(false)
                    : await LookUpSeriesAsync(target, title, year, ids, cancellationToken, forRequester: true).ConfigureAwait(false);
                return item is not null && Int(item["id"]) is > 0 ? LooksAfter(item, type, seasons) : error is null ? false : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not ask {Server} whether it has a title", target.Name);
                return null;
            }
        })).ConfigureAwait(false);

        return answers.Any(a => a == true) ? true : answers.Any(a => a is null) ? null : false;
    }

    /// <summary>
    /// Gets whether a title Radarr or Sonarr holds is still looked after there: a movie monitored or with its file; a
    /// monitored show whose every season asked for is monitored or has all its episodes. A season it doesn't list
    /// yet isn't looked after; a show that lists none is, as nothing says otherwise.
    /// </summary>
    /// <param name="item">The movie or series as the server returns it.</param>
    /// <param name="type">The type.</param>
    /// <param name="seasons">The seasons of a show asked for; null for all of them but specials.</param>
    /// <returns>Whether it is looked after.</returns>
    public static bool LooksAfter(JsonObject item, RequestType type, IReadOnlyCollection<int>? seasons)
    {
        ArgumentNullException.ThrowIfNull(item);
        var monitored = item["monitored"] is JsonValue m && m.TryGetValue<bool>(out var on) && on;
        if (type == RequestType.Movie)
        {
            return monitored || (item["hasFile"] is JsonValue f && f.TryGetValue<bool>(out var has) && has);
        }

        if (item["seasons"] is not JsonArray list || list.Count == 0)
        {
            return true;
        }

        var held = list.OfType<JsonObject>().GroupBy(s => Int(s["seasonNumber"]) ?? 0).ToDictionary(g => g.Key, g => g.First());
        var wanted = seasons ?? held.Keys.Where(n => n > 0).ToList();
        return wanted.Count > 0 && wanted.All(n => held.TryGetValue(n, out var season) && (
            (monitored && season["monitored"] is JsonValue sm && sm.TryGetValue<bool>(out var seasonOn) && seasonOn)
            || (Int(season["statistics"]?["episodeFileCount"]) is { } files && files > 0 && files >= (Int(season["statistics"]?["totalEpisodeCount"]) ?? Int(season["statistics"]?["episodeCount"]) ?? int.MaxValue))));
    }

    // Episodes per season (specials left out): those Sonarr wants once it monitors the season, else every one it lists.
    private static Dictionary<int, int>? EpisodesBySeason(JsonObject series)
    {
        var counts = (series["seasons"] as JsonArray)?.OfType<JsonObject>()
            .Select(s => (Number: Int(s["seasonNumber"]) ?? 0, Count: Int(s["statistics"]?["episodeCount"]) is > 0 and var c ? c : Int(s["statistics"]?["totalEpisodeCount"]) ?? 0))
            .Where(s => s.Number > 0 && s.Count > 0)
            .ToDictionary(s => s.Number, s => s.Count);
        return counts is { Count: > 0 } ? counts : null;
    }

    private static string? Text(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { Length: > 0 } text ? text.Trim() : null;

    /// <summary>
    /// Reads how Radarr or Sonarr is getting on with a request: its download progress, or else whether it
    /// already has the files or is still waiting for a release.
    /// </summary>
    /// <param name="request">An approved request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status, marked missing when the server does not have the title; null when it is not set up or unreachable.</returns>
    public async Task<ArrStatus?> GetStatusAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Target.For(request) is not { } target)
        {
            return null;
        }

        var status = await ReadStatusAsync(target, request, cancellationToken).ConfigureAwait(false);

        // A movie's file just read here is known at once to lists and search too, which would otherwise go on saying
        // it's still waiting until the answer they keep lapses or the library is read again.
        if (status is { Missing: false, Files: > 0 } && request.Type == RequestType.Movie
            && request.ProviderIds.GetValueOrDefault(ProviderIds.Tmdb) is { Length: > 0 } id)
        {
            NoteFiles(target, request.Type, id);
        }

        return status is null ? null : status with { ServerId = target.ServerId, ProfileId = target.ProfileId };
    }

    // Keeps that a server has a title's files, in the answer kept for it and in its library as last read.
    private void NoteFiles(Target target, RequestType type, string id)
    {
        var key = LibraryKey(target, type);
        _held[key + ":" + id] = (DateTime.UtcNow, true, true, true);
        if (_libraries.TryGetValue(key, out var library) && !library.Files.GetValueOrDefault(id))
        {
            _libraries.TryUpdate(key, library with { Files = new Dictionary<string, bool>(library.Files, StringComparer.Ordinal) { [id] = true } }, library);
        }
    }

    // lookUp finds the title again by its ids rather than the id it was sent with.
    private async Task<ArrStatus?> ReadStatusAsync(Target target, MediaRequest request, CancellationToken cancellationToken, bool lookUp = false)
    {
        // Its id only means something on the server it was sent to; on one that took over, it is looked up again. A
        // request from before servers were recorded was sent to the first listed, which is the one it resolves to.
        var arrId = lookUp || (request.ServerId is not null && target.ServerId != request.ServerId) ? null : request.ArrId;
        if (arrId is null)
        {
            var lookup = request.Type == RequestType.Movie
                ? await LookUpMovieAsync(target, request, cancellationToken).ConfigureAwait(false)
                : await LookUpSeriesAsync(target, request.Title, request.Year, request.ProviderIds, cancellationToken).ConfigureAwait(false);
            if (lookup.Error is not null)
            {
                return null;
            }

            arrId = Int(lookup.Item?["id"]);
            if (arrId is not > 0)
            {
                return new ArrStatus(null, null, "Not sent yet", Missing: true);
            }
        }

        var isMovie = request.Type == RequestType.Movie;
        var queue = await SendAsync(target, HttpMethod.Get, (isMovie ? "/api/v3/queue/details?movieId=" : "/api/v3/queue/details?includeEpisode=true&seriesId=") + arrId.Value.ToString(CultureInfo.InvariantCulture), cancellationToken, retries: 1).ConfigureAwait(false);
        if (!queue.Ok)
        {
            return null;
        }

        var records = (Parse(queue.Body) as JsonArray)?.OfType<JsonObject>()
            .Where(r => isMovie || request.Seasons is null || request.Seasons.Contains(Int(r["seasonNumber"]) ?? Int(r["episode"]?["seasonNumber"]) ?? -1))
            .ToList() ?? new List<JsonObject>();
        if (records.Count > 0)
        {
            var size = records.Sum(r => Double(r["size"]));
            var left = records.Sum(r => Double(r["sizeleft"]));
            var progress = size > 0 ? Math.Clamp(1 - (left / size), 0, 1) : (double?)null;
            var state = records.Select(r => r["trackedDownloadState"]?.GetValue<string>() ?? r["status"]?.GetValue<string>()).FirstOrDefault(s => !string.IsNullOrEmpty(s));

            // When the last of it should be done, as the download client estimates it.
            var done = records.Select(r => DateTime.TryParse(r["estimatedCompletionTime"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : (DateTime?)null)
                .Where(d => d is not null).Max();
            var text = state switch
            {
                "importPending" or "importing" => "Importing",
                "failedPending" or "failed" => "Retrying download",
                "paused" => "Paused",
                "queued" or "delay" => "Queued",
                _ => progress is { } p ? "Downloading " + (p * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "Downloading"
            };
            if (isMovie)
            {
                return new ArrStatus(arrId, progress, text) { DoneUtc = done };
            }

            // A show's seasons from the series itself, with what the queue holds of each; without it, none.
            var downloading = records.GroupBy(r => Int(r["seasonNumber"]) ?? Int(r["episode"]?["seasonNumber"]) ?? -1).ToDictionary(g => g.Key, g => g.Count());
            var show = await SendAsync(target, HttpMethod.Get, "/api/v3/series/" + arrId.Value.ToString(CultureInfo.InvariantCulture), cancellationToken, retries: 1).ConfigureAwait(false);
            return new ArrStatus(arrId, progress, text + EpisodeCount(records))
            {
                DoneUtc = done,
                Seasons = show.Ok && Parse(show.Body) is JsonObject series ? SeasonsOf(series, request, downloading) : null
            };
        }

        var item = await SendAsync(target, HttpMethod.Get, (isMovie ? "/api/v3/movie/" : "/api/v3/series/") + arrId.Value.ToString(CultureInfo.InvariantCulture), cancellationToken, retries: 1).ConfigureAwait(false);
        if (item.Status == 404)
        {
            // Removed there. Re-added since, it has a new id, so it is looked for again, once; otherwise it stays gone.
            var again = lookUp ? null : await ReadStatusAsync(target, request, cancellationToken, lookUp: true).ConfigureAwait(false);
            return again is null or { Missing: true } ? new ArrStatus(null, null, "No longer downloading", Missing: true) : again;
        }

        if (!item.Ok || Parse(item.Body) is not JsonObject found)
        {
            return null;
        }

        if (isMovie)
        {
            var hasFile = found["hasFile"]?.GetValue<bool>() == true;
            return new ArrStatus(arrId, hasFile ? 1 : null, hasFile ? "Awaiting library scan" : "Searching")
            {
                Folder = Text(found["path"]),
                Files = hasFile ? 1 : 0
            };
        }

        if (request.Specials is { Count: > 0 } specials)
        {
            // Specials are counted one by one, as Sonarr has each: its season 0 holds others too.
            var (episodes, _) = await SpecialsOfAsync(target, arrId.Value, wait: false, cancellationToken).ConfigureAwait(false);
            if (episodes is null)
            {
                return null;
            }

            var files = MatchSpecials(episodes, specials).Count(e => e["hasFile"]?.GetValueKind() == JsonValueKind.True);
            return new ArrStatus(
                arrId,
                (double)files / specials.Count,
                files >= specials.Count ? "Awaiting library scan"
                    : files > 0 ? files.ToString(CultureInfo.InvariantCulture) + "/" + specials.Count.ToString(CultureInfo.InvariantCulture) + " specials"
                    : "Searching",
                ExpectedEpisodes: EpisodesBySeason(found))
            {
                Folder = Text(found["path"]),
                Files = files,
                Seasons = new List<SeasonProgress> { new() { Season = 0, Files = files, Episodes = specials.Count } }
            };
        }

        var wanted = (found["seasons"] as JsonArray)?.OfType<JsonObject>()
            .Where(s => (Int(s["seasonNumber"]) ?? 0) > 0 && (request.Seasons is null || request.Seasons.Contains(Int(s["seasonNumber"]) ?? -1)))
            .ToList() ?? new List<JsonObject>();
        var have = wanted.Sum(s => Int(s["statistics"]?["episodeFileCount"]) ?? 0);
        var total = wanted.Sum(s => Int(s["statistics"]?["episodeCount"]) ?? 0);
        return new ArrStatus(
            arrId,
            total > 0 ? (double)have / total : null,
            total > 0 && have >= total ? "Awaiting library scan"
                : have > 0 ? have.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + " episodes"
                : "Searching",
            ExpectedEpisodes: EpisodesBySeason(found))
        {
            Folder = Text(found["path"]),
            Files = have,
            Seasons = SeasonsOf(found, request, new Dictionary<int, int>())
        };
    }

    // Each requested season (every season past the specials for a whole show) as Sonarr has it: files, episodes, and
    // what the queue is downloading of it.
    private static List<SeasonProgress> SeasonsOf(JsonObject series, MediaRequest request, Dictionary<int, int> downloading)
        => ((series["seasons"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Select(s => (Number: Int(s["seasonNumber"]) ?? 0, Stats: s["statistics"]))
            .Where(s => s.Number > 0 && (request.Seasons is null || request.Seasons.Contains(s.Number)))
            .OrderBy(s => s.Number)
            .Select(s => new SeasonProgress
            {
                Season = s.Number,
                Files = Int(s.Stats?["episodeFileCount"]) ?? 0,
                Episodes = Int(s.Stats?["episodeCount"]) ?? 0,
                Downloading = downloading.TryGetValue(s.Number, out var n) ? n : 0
            })
            .ToList();

    /// <summary>
    /// Tries a connection and returns its quality profiles and root folders for the settings page.
    /// </summary>
    /// <param name="url">The server address.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the server reported, or why it could not be reached.</returns>
    public async Task<ArrOptions> TestAsync(string url, string apiKey, CancellationToken cancellationToken)
    {
        if (!TryNormalizeUrl(url, out var baseUrl))
        {
            return new ArrOptions { Error = ServerWording.Get(TranslationKeys.RequestsErrorAddressScheme) };
        }

        var target = new Target(null, ServerWording.Get(TranslationKeys.RequestsValueTheServer), baseUrl, apiKey, 0, string.Empty);
        var status = await SendAsync(target, HttpMethod.Get, "/api/v3/system/status", cancellationToken).ConfigureAwait(false);
        if (!status.Ok)
        {
            return new ArrOptions { Error = Describe(target.Name, status) };
        }

        var options = new ArrOptions { Ok = true, Version = Parse(status.Body)?["version"]?.GetValue<string>() };

        var profiles = await SendAsync(target, HttpMethod.Get, "/api/v3/qualityprofile", cancellationToken).ConfigureAwait(false);
        if (profiles.Ok && Parse(profiles.Body) is JsonArray profileList)
        {
            options.Profiles = profileList
                .OfType<JsonObject>()
                .Select(p => new ArrProfile { Id = Int(p["id"]) ?? 0, Name = p["name"]?.GetValue<string>() ?? string.Empty })
                .Where(p => p.Id > 0)
                .ToList();
        }

        var folders = await SendAsync(target, HttpMethod.Get, "/api/v3/rootfolder", cancellationToken).ConfigureAwait(false);
        if (folders.Ok && Parse(folders.Body) is JsonArray folderList)
        {
            options.RootFolders = folderList
                .OfType<JsonObject>()
                .Select(f => f["path"]?.GetValue<string>())
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();
        }

        // Tags are optional: a server whose tags can't be read still connects, and its profiles just offer none.
        var tags = await SendAsync(target, HttpMethod.Get, "/api/v3/tag", cancellationToken).ConfigureAwait(false);
        if (tags.Ok)
        {
            options.Tags = MapTags(tags.Body);
        }

        return options;
    }

    /// <summary>Maps Radarr's or Sonarr's tag list, each tag once, by label.</summary>
    /// <param name="body">The server's answer: a JSON array of { id, label }.</param>
    /// <returns>The tags; empty when the answer isn't a list.</returns>
    internal static List<ArrTag> MapTags(string body)
        => (Parse(body) as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Select(t => new ArrTag { Id = Int(t["id"]) ?? 0, Label = Text(t["label"]) ?? string.Empty })
            .Where(t => t.Id > 0 && t.Label.Length > 0)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Returns where an administrator opens a request's title in Radarr or Sonarr: its page at the server's browser
    /// address (its External URL, else its address). Radarr's movie pages go by TMDb id; Sonarr's by the show's
    /// slug, which is read from Sonarr. Null when the title isn't known there or its server isn't set up.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page and the server's name, or null.</returns>
    public async Task<ArrLink?> LinkAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Target.For(request) is not { } target)
        {
            return null;
        }

        if (request.Type == RequestType.Movie && request.ProviderIds.TryGetValue(ProviderIds.Tmdb, out var tmdb) && int.TryParse(tmdb, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return new ArrLink(target.Link + "/movie/" + tmdb, target.Name);
        }

        // Its own record when it was sent to this server, else whatever the server finds by its ids. Read as a lookup,
        // so an administrator opening requests never trips the breaker sends and the status sync rely on.
        JsonObject? item = null;
        if (request.ArrId is { } arrId && (request.ServerId is null || request.ServerId == target.ServerId))
        {
            var own = await SendAsync(target, HttpMethod.Get, (request.Type == RequestType.Movie ? "/api/v3/movie/" : "/api/v3/series/") + arrId.ToString(CultureInfo.InvariantCulture), cancellationToken, forRequester: true).ConfigureAwait(false);
            item = own.Ok ? Parse(own.Body) as JsonObject : null;
        }

        if (item is null)
        {
            var lookup = request.Type == RequestType.Movie
                ? await LookUpMovieAsync(target, request, cancellationToken, forRequester: true).ConfigureAwait(false)
                : await LookUpSeriesAsync(target, request.Title, request.Year, request.ProviderIds, cancellationToken, forRequester: true).ConfigureAwait(false);
            item = Int(lookup.Item?["id"]) is > 0 ? lookup.Item : null;
        }

        var slug = request.Type == RequestType.Movie ? Int(item?["tmdbId"])?.ToString(CultureInfo.InvariantCulture) ?? Text(item?["titleSlug"]) : Text(item?["titleSlug"]);
        return slug is null ? null : new ArrLink(target.Link + (request.Type == RequestType.Movie ? "/movie/" : "/series/") + Uri.EscapeDataString(slug), target.Name);
    }

    /// <summary>
    /// Returns where an administrator opens a title in the Radarr (movies) or Sonarr (shows) that has it, asking each
    /// server of its type in turn, the default first: a movie's page by its TMDb id, a show's by its slug, found by its
    /// TVDB id. Null when none has it, or it has neither id.
    /// </summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="ids">The title's provider ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page and the server's name, or null.</returns>
    public async Task<ArrLink?> HoldingLinkAsync(RequestType type, IReadOnlyDictionary<string, string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var key = type == RequestType.Movie ? ProviderIds.Tmdb : ProviderIds.Tvdb;
        if (!ids.TryGetValue(key, out var id) || !int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return null;
        }

        var query = (type == RequestType.Movie ? "/api/v3/movie?tmdbId=" : "/api/v3/series?tvdbId=") + number.ToString(CultureInfo.InvariantCulture);
        foreach (var target in Target.AllFor(type))
        {
            var answer = await SendAsync(target, HttpMethod.Get, query, cancellationToken, forRequester: true).ConfigureAwait(false);
            if (!answer.Ok || (Parse(answer.Body) as JsonArray)?.OfType<JsonObject>().FirstOrDefault() is not { } held)
            {
                continue;
            }

            var slug = type == RequestType.Movie ? number.ToString(CultureInfo.InvariantCulture) : Text(held["titleSlug"]);
            if (slug is not null)
            {
                return new ArrLink(target.Link + (type == RequestType.Movie ? "/movie/" : "/series/") + Uri.EscapeDataString(slug), target.Name);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks and trims a server address, dropping any trailing slash.
    /// </summary>
    /// <param name="url">The address as entered.</param>
    /// <param name="normalized">The usable address.</param>
    /// <returns>True when it is an http or https address.</returns>
    public static bool TryNormalizeUrl(string? url, out string normalized)
    {
        normalized = (url ?? string.Empty).Trim().TrimEnd('/');
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private async Task<(int? ArrId, string? Error)> AddNewAsync(Target target, MediaRequest request, JsonObject item, CancellationToken cancellationToken)
    {
        PrepareNew(item, request.Type, target.QualityProfileId, target.RootFolder, target.Add, request.Seasons);
        if (request.Specials is not null)
        {
            // Only the specials asked for are wanted: no season is monitored, and nothing is searched for on adding.
            MonitorSeasons(item, new List<int>());
            item["addOptions"] = new JsonObject { ["searchForMissingEpisodes"] = false, ["monitor"] = "none" };
        }

        var path = request.Type == RequestType.Movie ? "/api/v3/movie" : "/api/v3/series";
        var result = await SendAsync(target, HttpMethod.Post, path, cancellationToken, item.ToJsonString()).ConfigureAwait(false);
        return result.Ok ? (Int(Parse(result.Body)?["id"]), null) : (null, Describe(target.Name, result));
    }

    /// <summary>
    /// Fills in a looked-up title to add it: the profile's quality profile, root folder and tags, monitored, and
    /// Radarr's minimum availability or Sonarr's series type, season folders and seasons, with a search when the
    /// server searches on add.
    /// </summary>
    /// <param name="item">The title as Radarr or Sonarr looked it up, changed in place.</param>
    /// <param name="type">Movie for Radarr, Series for Sonarr.</param>
    /// <param name="qualityProfileId">The server's quality profile.</param>
    /// <param name="rootFolder">The server's root folder.</param>
    /// <param name="add">The server's and profile's add settings.</param>
    /// <param name="seasons">For a show, the seasons asked for, or null for all of it.</param>
    internal static void PrepareNew(JsonObject item, RequestType type, int qualityProfileId, string rootFolder, AddSettings add, List<int>? seasons)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(add);
        item["qualityProfileId"] = qualityProfileId;
        item["rootFolderPath"] = rootFolder;
        item["monitored"] = true;

        // A lookup result can carry tags of its own (none, usually); the profile's are added to them, never in place of them.
        item["tags"] = MergeTags(item["tags"], add.Tags);
        if (type == RequestType.Movie)
        {
            item["minimumAvailability"] = add.MinimumAvailability switch
            {
                ArrMinimumAvailability.Announced => "announced",
                ArrMinimumAvailability.InCinemas => "inCinemas",
                _ => "released"
            };
            item["addOptions"] = new JsonObject { ["searchForMovie"] = add.Search };
        }
        else
        {
            item["seasonFolder"] = add.SeasonFolder;
            item["seriesType"] = add.SeriesType switch
            {
                ArrSeriesType.Daily => "daily",
                ArrSeriesType.Anime => "anime",
                _ => "standard"
            };
            MonitorSeasons(item, seasons);

            // Whole shows monitor everything; for chosen seasons the per-season flags above decide.
            var addOptions = new JsonObject { ["searchForMissingEpisodes"] = add.Search };
            if (seasons is null)
            {
                addOptions["monitor"] = "all";
            }

            item["addOptions"] = addOptions;
        }
    }

    /// <summary>A title's tags with more added: the ones it has, in their order, then each new one it lacks.</summary>
    /// <param name="existing">The title's tags as the server gave them, or null.</param>
    /// <param name="add">The tags to add.</param>
    /// <returns>The tags.</returns>
    internal static JsonArray MergeTags(JsonNode? existing, IEnumerable<int> add)
    {
        ArgumentNullException.ThrowIfNull(add);
        var tags = (existing as JsonArray ?? new JsonArray()).Select(Int).Where(t => t is > 0).Select(t => t!.Value).ToList();
        tags.AddRange(add.Where(t => t > 0 && !tags.Contains(t)).Distinct());
        return new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
    }

    /// <summary>
    /// The searches to start for a title already in Radarr or Sonarr: the movie, the whole show, or each season asked for,
    /// so a show's request never searches seasons the administrator monitors for their own reasons.
    /// </summary>
    /// <param name="type">Movie for Radarr, Series for Sonarr.</param>
    /// <param name="id">The title's id there.</param>
    /// <param name="seasons">For a show, the seasons asked for, or null for all of it.</param>
    /// <returns>The command bodies, in order.</returns>
    internal static List<JsonObject> SearchCommands(RequestType type, int id, List<int>? seasons)
    {
        if (type == RequestType.Movie)
        {
            return new List<JsonObject> { new() { ["name"] = "MoviesSearch", ["movieIds"] = new JsonArray(id) } };
        }

        return seasons is null
            ? new List<JsonObject> { new() { ["name"] = "SeriesSearch", ["seriesId"] = id } }
            : seasons.Distinct().OrderBy(n => n).Select(n => new JsonObject { ["name"] = "SeasonSearch", ["seriesId"] = id, ["seasonNumber"] = n }).ToList();
    }

    private async Task<(int? ArrId, string? Error)> SearchExistingAsync(Target target, MediaRequest request, JsonObject item, int id, CancellationToken cancellationToken)
    {
        var isMovie = request.Type == RequestType.Movie;
        var path = (isMovie ? "/api/v3/movie/" : "/api/v3/series/") + id.ToString(CultureInfo.InvariantCulture);
        if (isMovie && item["hasFile"]?.GetValue<bool>() == true)
        {
            return (id, null);
        }

        // An unmonitored movie would be searched once and then ignored by RSS sync, so it's monitored first, and given
        // the profile's tags. A show always is: its requested seasons are switched on without turning off any the
        // administrator already monitors. Tags are only ever added; ones it has stay.
        var missingTags = target.Add.Tags.Any(t => !(item["tags"] as JsonArray ?? new JsonArray()).Select(Int).Contains(t));
        if (!isMovie || item["monitored"]?.GetValue<bool>() != true || missingTags)
        {
            var current = await SendAsync(target, HttpMethod.Get, path, cancellationToken, retries: 1).ConfigureAwait(false);
            if (!current.Ok || Parse(current.Body) is not JsonObject title)
            {
                return (null, Describe(target.Name, current));
            }

            title["monitored"] = true;
            title["tags"] = MergeTags(title["tags"], target.Add.Tags);
            if (!isMovie && request.Specials is null)
            {
                MonitorSeasons(title, request.Seasons, keepMonitored: true);
            }

            var update = await SendAsync(target, HttpMethod.Put, path, cancellationToken, title.ToJsonString(), retries: 1).ConfigureAwait(false);
            if (!update.Ok)
            {
                return (null, Describe(target.Name, update));
            }
        }

        // Specials are monitored and searched for one by one, never as the whole of season 0.
        if (request.Specials is not null)
        {
            return await SendSpecialsAsync(target, request, id, justAdded: false, cancellationToken).ConfigureAwait(false);
        }

        // A server that doesn't search on add leaves it monitored, for its RSS sync or a search started there.
        if (!target.Add.Search)
        {
            return (id, null);
        }

        foreach (var command in SearchCommands(request.Type, id, request.Seasons))
        {
            var search = await SendAsync(target, HttpMethod.Post, "/api/v3/command", cancellationToken, command.ToJsonString(), retries: 1).ConfigureAwait(false);
            if (!search.Ok)
            {
                return (null, Describe(target.Name, search));
            }
        }

        return (id, null);
    }

    // Sonarr's episodes for a show's specials (its season 0). A show just added has none until Sonarr has read its
    // episodes, which takes it a few seconds, so that's waited for a little.
    private async Task<(List<JsonObject>? Episodes, string? Error)> SpecialsOfAsync(Target target, int seriesId, bool wait, CancellationToken cancellationToken)
    {
        var path = "/api/v3/episode?seasonNumber=0&seriesId=" + seriesId.ToString(CultureInfo.InvariantCulture);
        for (var attempt = 0; ; attempt++)
        {
            var result = await SendAsync(target, HttpMethod.Get, path, cancellationToken, retries: 1).ConfigureAwait(false);
            if (!result.Ok)
            {
                return (null, Describe(target.Name, result));
            }

            var episodes = (Parse(result.Body) as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            if (episodes.Count > 0 || !wait || attempt >= 5)
            {
                return (episodes, null);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Finds Sonarr's episode for each special asked for: the one that aired the same day when there's just one, as
    /// Sonarr numbers a show's specials by TVDB and a request by TMDb, which don't always agree; else the one of the
    /// same number.
    /// </summary>
    /// <param name="episodes">Sonarr's episodes of the show's season 0.</param>
    /// <param name="specials">The specials asked for.</param>
    /// <returns>Sonarr's episodes for them, each once; fewer when Sonarr doesn't list some.</returns>
    internal static List<JsonObject> MatchSpecials(IReadOnlyList<JsonObject> episodes, IEnumerable<SpecialEpisode> specials)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(specials);
        var found = new List<JsonObject>();
        foreach (var special in specials)
        {
            var day = special.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sameDay = day is null ? new List<JsonObject>() : episodes.Where(e => Text(e["airDate"]) == day).ToList();
            var match = sameDay.Count == 1 ? sameDay[0] : episodes.FirstOrDefault(e => Int(e["episodeNumber"]) == special.Number);
            if (match is not null && !found.Contains(match))
            {
                found.Add(match);
            }
        }

        return found;
    }

    // Switches the specials asked for on in Sonarr, each on its own, and searches for those without a file when the
    // server searches on add. The show stays as it is otherwise: no season is switched on or off.
    private async Task<(int? ArrId, string? Error)> SendSpecialsAsync(Target target, MediaRequest request, int seriesId, bool justAdded, CancellationToken cancellationToken)
    {
        var (episodes, error) = await SpecialsOfAsync(target, seriesId, justAdded, cancellationToken).ConfigureAwait(false);
        if (episodes is null)
        {
            return (null, error);
        }

        var wanted = MatchSpecials(episodes, request.Specials ?? new List<SpecialEpisode>());
        var ids = wanted.Select(e => Int(e["id"])).OfType<int>().Where(id => id > 0).ToList();
        if (ids.Count == 0)
        {
            // A show just added may not have its episodes yet: the send is tried again, as any that fails is.
            return (null, ServerWording.Format(episodes.Count == 0 ? TranslationKeys.RequestsErrorArrNoSpecialsYet : TranslationKeys.RequestsErrorArrSpecialsMissing, target.Name));
        }

        var body = new JsonObject { ["episodeIds"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()), ["monitored"] = true };
        var monitor = await SendAsync(target, HttpMethod.Put, "/api/v3/episode/monitor", cancellationToken, body.ToJsonString(), retries: 1).ConfigureAwait(false);
        if (!monitor.Ok)
        {
            return (null, Describe(target.Name, monitor));
        }

        var missing = wanted.Where(e => e["hasFile"]?.GetValueKind() != JsonValueKind.True).Select(e => Int(e["id"])).OfType<int>().ToList();
        if (target.Add.Search && missing.Count > 0)
        {
            var command = new JsonObject { ["name"] = "EpisodeSearch", ["episodeIds"] = new JsonArray(missing.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) };
            var search = await SendAsync(target, HttpMethod.Post, "/api/v3/command", cancellationToken, command.ToJsonString(), retries: 1).ConfigureAwait(false);
            if (!search.Ok)
            {
                return (null, Describe(target.Name, search));
            }
        }

        return (seriesId, null);
    }

    private static void MonitorSeasons(JsonObject series, List<int>? seasons, bool keepMonitored = false)
    {
        if (series["seasons"] is not JsonArray list)
        {
            return;
        }

        foreach (var season in list.OfType<JsonObject>())
        {
            var number = Int(season["seasonNumber"]) ?? 0;
            var wanted = seasons is null ? number > 0 : seasons.Contains(number);
            var already = keepMonitored && season["monitored"]?.GetValue<bool>() == true;
            season["monitored"] = wanted || already;
        }
    }

    // Radarr's lookup/tmdb and lookup/imdb never consult its library, so a movie it already has would come back
    // without its id. The term search ("tmdb:603", "imdb:tt0133093") does, so it is used for both.
    private async Task<(JsonObject? Item, string? Error)> LookUpMovieAsync(Target target, MediaRequest request, CancellationToken cancellationToken, bool forRequester = false)
    {
        foreach (var (key, prefix, field) in new[] { (ProviderIds.Tmdb, "tmdb:", "tmdbId"), (ProviderIds.Imdb, "imdb:", "imdbId") })
        {
            if (!request.ProviderIds.TryGetValue(key, out var id))
            {
                continue;
            }

            var result = await SendAsync(target, HttpMethod.Get, "/api/v3/movie/lookup?term=" + Uri.EscapeDataString(prefix + id), cancellationToken, retries: 1, forRequester: forRequester).ConfigureAwait(false);
            if (!result.Ok && result.Status != 404)
            {
                return (null, Describe(target.Name, result));
            }

            var movie = (Parse(result.Body) as JsonArray)?.OfType<JsonObject>().FirstOrDefault(m => SameId(m, field, request.ProviderIds, key));
            if (movie is not null)
            {
                return (movie, null);
            }
        }

        return (null, null);
    }

    private static string NotFound(Target target, RequestType type)
        => ServerWording.Format(type == RequestType.Movie ? TranslationKeys.RequestsErrorArrMovieNotFound : TranslationKeys.RequestsErrorArrShowNotFound, target.Name);

    private async Task<(JsonObject? Item, string? Error)> LookUpSeriesAsync(Target target, string title, int? year, Dictionary<string, string> ids, CancellationToken cancellationToken, bool forRequester = false)
    {
        // Sonarr looks shows up by TVDb id; without one, search by title and pick the result whose ids match.
        var term = ids.TryGetValue(ProviderIds.Tvdb, out var tvdb) ? "tvdb:" + tvdb : title;
        var result = await SendAsync(target, HttpMethod.Get, "/api/v3/series/lookup?term=" + Uri.EscapeDataString(term), cancellationToken, retries: 1, forRequester: forRequester).ConfigureAwait(false);
        if (!result.Ok)
        {
            return (null, Describe(target.Name, result));
        }

        var candidates = (Parse(result.Body) as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        var match = candidates.FirstOrDefault(c => SameId(c, "tvdbId", ids, ProviderIds.Tvdb))
            ?? candidates.FirstOrDefault(c => SameId(c, "tmdbId", ids, ProviderIds.Tmdb))
            ?? candidates.FirstOrDefault(c => SameId(c, "imdbId", ids, ProviderIds.Imdb))
            ?? candidates.FirstOrDefault(c => year.HasValue
                && string.Equals(c["title"]?.GetValue<string>(), title, StringComparison.OrdinalIgnoreCase)
                && Int(c["year"]) == year);
        return (match, null);
    }

    private static bool SameId(JsonObject candidate, string field, Dictionary<string, string> ids, string key)
    {
        if (!ids.TryGetValue(key, out var wanted))
        {
            return false;
        }

        var value = candidate[field];
        var text = value?.GetValueKind() switch
        {
            JsonValueKind.Number => value.GetValue<long>().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => value.GetValue<string>(),
            _ => null
        };
        return string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string EpisodeCount(List<JsonObject> records)
        => " (" + ActivityText.Count(records.Count, "episode") + ")";

    // Unreachable servers and 5xx/429 answers are thrown as HttpRequestException so RetryPolicy retries them;
    // anything else comes back as the answer. The breaker is only consulted for the configured servers, and a lookup
    // a requester drives uses that server's lookup breaker rather than the one sends and the status sync use.
    private async Task<HttpResult> SendAsync(Target target, HttpMethod method, string path, CancellationToken cancellationToken, string? body = null, int retries = 0, bool forRequester = false, bool forMarking = false)
    {
        var breakers = forMarking ? _markingBreakers : forRequester ? _lookupBreakers : _breakers;
        var breaker = target.ServerId is { } server ? breakers.GetOrAdd(server, _ => new CircuitBreaker(_logger, target.Name + (forMarking ? " list marking" : forRequester ? " lookups" : string.Empty), 5, TimeSpan.FromMinutes(10))) : null;
        if (breaker is not null && !breaker.AllowOperation(out _))
        {
            return new HttpResult(0, string.Empty);
        }

        HttpResult result;
        try
        {
            result = await RetryPolicy.ExecuteWithRetryAsync(
                async ct =>
                {
                    var r = await StolonHttp.SendAsync(
                        _httpClientFactory,
                        method,
                        target.Url + path,
                        ct,
                        headers: new[] { new KeyValuePair<string, string>("X-Api-Key", target.ApiKey) },
                        body: body).ConfigureAwait(false);
                    if (r.Status == 0 || r.Status >= 500 || r.Status == 429)
                    {
                        throw new TransientArrException(r);
                    }

                    return r;
                },
                retries,
                _logger,
                target.Name + " " + method + " " + path.Split('?')[0],
                cancellationToken).ConfigureAwait(false);
        }
        catch (TransientArrException ex)
        {
            result = ex.Result;
        }

        if (result.Status == 0 || result.Status >= 500)
        {
            breaker?.RecordFailure(target.Name + " answered " + result.Status.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            breaker?.RecordSuccess();
        }

        if (!result.Ok && result.Status != 404)
        {
            _logger.LogWarning("{Server} {Method} {Path} answered {Status}", target.Name, method, path.Split('?')[0], result.Status);
        }

        return result;
    }

    private static string Describe(string name, HttpResult result) => result.Status switch
    {
        0 => ServerWording.Format(TranslationKeys.RequestsErrorCouldNotReach, name),
        401 => ServerWording.Format(TranslationKeys.RequestsErrorArrRefusedKey, name),
        _ => ServerWording.Format(TranslationKeys.RequestsErrorArrAnswered, name, result.Status.ToString(CultureInfo.InvariantCulture), FormatUtilities.SanitizeForLog(FirstMessage(result.Body)))
    };

    // Radarr and Sonarr report validation failures as a list of { errorMessage }.
    private static string FirstMessage(string body)
    {
        var node = Parse(body);
        var message = (node as JsonArray)?.OfType<JsonObject>().Select(e => e["errorMessage"]?.GetValue<string>()).FirstOrDefault(m => !string.IsNullOrEmpty(m))
            ?? (node as JsonObject)?["message"]?.GetValue<string>();
        return message ?? StolonHttp.FirstLine(body);
    }

    private static int? Int(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value) ? value : null;

    private static double Double(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<double>(out var value) ? value : 0;

    private static JsonNode? Parse(string body)
    {
        try
        {
            return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A server to talk to, with the profile titles are added with. A connection test has no server or profile.
    private sealed record Target(string? ServerId, string Name, string Url, string ApiKey, int QualityProfileId, string RootFolder, string? ProfileId = null)
    {
        // How titles are added: whether the server searches, and the profile's tags and type-specific settings.
        public AddSettings Add { get; init; } = AddSettings.Defaults;

        // Where an administrator opens the server in a browser: its External URL, else the address the plugin uses.
        public string Link { get; init; } = string.Empty;

        // Each configuration's servers as last read into targets, so marking a list or checking a title doesn't decrypt
        // every server's key again each time. Read again once the configuration is replaced (any settings save) or its
        // servers are; a server's own key is decrypted again only when it changes (see Key).
        private static readonly ConditionalWeakTable<RequestSettings, ReadTargets> _read = new();
        private static readonly ConditionalWeakTable<ArrServer, DecryptedKey> _keys = new();

        // Every server set up for a type, each once (with its first profile), the default first.
        public static IReadOnlyList<Target> AllFor(RequestType type)
            => Read() is { } read ? (type == RequestType.Movie ? read.Movies : read.Shows) : Array.Empty<Target>();

        // The default server and its first profile for a type, for searching and looking titles up.
        public static Target? For(RequestType type)
            => Read() is { } read ? (type == RequestType.Movie ? read.Movie : read.Show) : null;

        // The servers of the configuration in use, as targets: kept for it while its servers stay the same list.
        private static ReadTargets? Read()
        {
            if (RequestConfig.Instance is not { } plugin)
            {
                return null;
            }

            // What the servers are, read under the configuration's lock; their keys decrypted after it.
            var (config, kept, usable) = plugin.ReadConfiguration(c => _read.TryGetValue(c, out var r) && ReferenceEquals(r.Servers, c.ArrServers)
                ? (c, r, (Usable?)null)
                : (c, null, new Usable(c.ArrServers, Each(c, RequestType.Movie), Each(c, RequestType.Series), ArrServers.Resolve(c, RequestType.Movie, null), ArrServers.Resolve(c, RequestType.Series, null))));
            if (kept is not null || usable is null)
            {
                return kept;
            }

            var read = new ReadTargets(
                usable.Servers,
                usable.Movies.Select(u => Make(u.Server, u.Profile)).OfType<Target>().ToList(),
                usable.Shows.Select(u => Make(u.Server, u.Profile)).OfType<Target>().ToList(),
                usable.Movie is { } movie ? Make(movie.Server, movie.Profile) : null,
                usable.Show is { } show ? Make(show.Server, show.Profile) : null);
            _read.AddOrUpdate(config, read);
            return read;
        }

        private static List<(ArrServer Server, ArrProfileOption Profile)> Each(RequestSettings config, RequestType type)
            => ArrServers.Usable(config, type).GroupBy(u => u.Server.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();

        // The server and profile a request goes to: its own while its server is set up. A server that's still there
        // but can't be used for now (its key being re-entered, say) gives none, so the request waits for it rather
        // than moving; only one that has been removed takes the default for its type. One sent before servers were
        // recorded went to the first listed, so it's followed there, whichever is the default now.
        public static Target? For(MediaRequest request)
        {
            var found = RequestConfig.Instance?.ReadConfiguration(c =>
            {
                var usable = ArrServers.Usable(c, request.Type).ToList();
                var mine = usable.Where(u => string.Equals(u.Server.Id, request.ServerId, StringComparison.Ordinal)).ToList();
                if (mine.FirstOrDefault(u => string.Equals(u.Profile.Id, request.ProfileId, StringComparison.Ordinal)) is { Server: not null } exact)
                {
                    return exact;
                }

                if (mine.Count > 0)
                {
                    return mine[0];
                }

                var stillThere = request.ServerId is not null && (c.ArrServers ?? new()).Any(s => s?.Id == request.ServerId && s.Type == request.Type);
                var sentUnrecorded = request.ServerId is null && request.ArrId is not null;
                return stillThere ? null : ArrServers.Resolve(c, request.Type, request.ProfileId, defaultFirst: !sentUnrecorded);
            });
            return found is { Server: not null } f ? Make(f.Server, f.Profile) : null;
        }

        // A server's key, decrypted once for as long as it's stored the same: decrypting is slow enough to notice when
        // every list and request asks for it.
        private static string Key(RequestConfig plugin, ArrServer server)
        {
            var stored = server.ApiKey;
            if (_keys.TryGetValue(server, out var kept) && string.Equals(kept.Stored, stored, StringComparison.Ordinal) && ReferenceEquals(kept.Secrets, plugin.Secrets))
            {
                return kept.Key;
            }

            var key = plugin.Secrets.Unprotect(stored);
            _keys.AddOrUpdate(server, new DecryptedKey(stored, plugin.Secrets, key));
            return key;
        }

        private static Target? Make(ArrServer server, ArrProfileOption profile)
            => TryNormalizeUrl(server.Url, out var url) && RequestConfig.Instance is { } plugin
                ? new Target(server.Id, string.IsNullOrWhiteSpace(server.Name) ? NameFor(server.Type) : server.Name, url, Key(plugin, server), profile.QualityProfileId, profile.RootFolder, profile.Id)
                {
                    Add = AddSettings.From(server, profile),
                    Link = TryNormalizeUrl(server.ExternalUrl, out var external) ? external : url
                }
                : null;
    }

    // A configuration's servers as they can be used, for each type: every server, and the default.
    private sealed record Usable(
        List<ArrServer>? Servers,
        List<(ArrServer Server, ArrProfileOption Profile)> Movies,
        List<(ArrServer Server, ArrProfileOption Profile)> Shows,
        (ArrServer Server, ArrProfileOption Profile)? Movie,
        (ArrServer Server, ArrProfileOption Profile)? Show);

    // A configuration's servers as targets, with the list they were read from.
    private sealed record ReadTargets(List<ArrServer>? Servers, IReadOnlyList<Target> Movies, IReadOnlyList<Target> Shows, Target? Movie, Target? Show);

    // A server's key as stored, the protector it was decrypted with, and the key.
    private sealed record DecryptedKey(string Stored, SecretProtector Secrets, string Key);

    /// <summary>Gets the servers set up for a type as they're talked to: name, address and key, the default first.</summary>
    /// <param name="type">Movies or shows.</param>
    /// <returns>The servers.</returns>
    internal static IReadOnlyList<(string Name, string Url, string ApiKey)> Servers(RequestType type)
        => Target.AllFor(type).Select(t => (t.Name, t.Url, t.ApiKey)).ToList();

    private sealed class TransientArrException : HttpRequestException
    {
        public TransientArrException(HttpResult result)
            : base("Answered " + result.Status.ToString(CultureInfo.InvariantCulture), null, result.Status == 0 ? null : (HttpStatusCode)result.Status)
        {
            Result = result;
        }

        public HttpResult Result { get; }
    }
}

/// <summary>Which of some titles Radarr or Sonarr has, and which of those it has the files of.</summary>
/// <param name="Ids">The TMDb ids of the movies, or the TVDB ids of the shows, it has.</param>
/// <param name="WithFiles">Those of them it has files for.</param>
public sealed record ArrHeld(HashSet<string> Ids, HashSet<string> WithFiles);

/// <summary>
/// How Radarr or Sonarr is getting on with a request.
/// </summary>
/// <param name="ArrId">The id it knows the title by, or null when it no longer has it.</param>
/// <param name="Progress">Download progress from 0 to 1, when known.</param>
/// <param name="Text">What it is doing, in words.</param>
/// <param name="Missing">True when the server does not have the title (never added, or since removed).</param>
/// <param name="ExpectedEpisodes">For a show, how many episodes each season has, when the server said.</param>
public sealed record ArrStatus(int? ArrId, double? Progress, string Text, bool Missing = false, Dictionary<int, int>? ExpectedEpisodes = null)
{
    /// <summary>Gets the server the status was read from.</summary>
    public string? ServerId { get; init; }

    /// <summary>Gets when the download should be done, as the download client estimates it; null when it doesn't say.</summary>
    public DateTime? DoneUtc { get; init; }

    /// <summary>Gets the profile on that server the request follows.</summary>
    public string? ProfileId { get; init; }

    /// <summary>Gets the title's folder as the server sees it, when it said.</summary>
    public string? Folder { get; init; }

    /// <summary>Gets how many of the request's files the server has: the movie's one, or the wanted seasons' episodes.</summary>
    public int Files { get; init; }

    /// <summary>Gets each requested season of a show as the server has it; null for a movie or when it couldn't say.</summary>
    public List<SeasonProgress>? Seasons { get; init; }
}

/// <summary>
/// How a server adds titles with one of its profiles: whether it searches straight away, the tags titles get, and
/// Radarr's minimum availability or Sonarr's series type and season folders.
/// </summary>
/// <param name="Search">Whether the server searches as soon as a title is sent.</param>
/// <param name="Tags">The server's tag ids titles get.</param>
/// <param name="MinimumAvailability">For Radarr, when a movie counts as available.</param>
/// <param name="SeriesType">For Sonarr, the series type.</param>
/// <param name="SeasonFolder">For Sonarr, whether episodes go in a folder per season.</param>
internal sealed record AddSettings(bool Search, IReadOnlyList<int> Tags, ArrMinimumAvailability MinimumAvailability, ArrSeriesType SeriesType, bool SeasonFolder)
{
    /// <summary>Gets the settings titles were added with before servers and profiles had any: a search, no tags, released, standard, season folders.</summary>
    public static AddSettings Defaults { get; } = new(true, Array.Empty<int>(), ArrMinimumAvailability.Released, ArrSeriesType.Standard, true);

    /// <summary>Gets a server's and profile's settings.</summary>
    /// <param name="server">The server.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The settings.</returns>
    public static AddSettings From(ArrServer server, ArrProfileOption profile)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(profile);
        return new(server.SearchOnAdd, (profile.Tags ?? new()).Where(t => t > 0).Distinct().ToList(), profile.MinimumAvailability, profile.SeriesType, profile.SeasonFolder);
    }
}
