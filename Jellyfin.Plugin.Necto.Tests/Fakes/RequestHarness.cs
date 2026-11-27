using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Necto.Api.Requests;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// The Requests and Issues services wired together over a database of their own, with the server faked: users are
/// those added here, the library holds what is added to it, Radarr is answered in memory, and Notifications keeps
/// every event. Everyone may request and report, as a server that has turned both on for everyone.
/// </summary>
public sealed class RequestHarness : IDisposable
{
    public const string RadarrUrl = "http://radarr.test";

    /// <summary>The fake server's id, which the request page is served with.</summary>
    public const string ServerId = "5e1f0c2a9b8d4e7f8a6b3c2d1e0f9a8b";

    private readonly string _folder = Path.Join(Path.GetTempPath(), "necto-requests-" + Guid.NewGuid().ToString("N"));
    private readonly ILibraryManager _library;

    public RequestHarness()
    {
        Directory.CreateDirectory(_folder);
        SQLitePCL.Batteries_V2.Init();
        Plugin = TestPlugin.CreateWithSettings();
        Plugin.MutateConfiguration(c =>
        {
            c.Everyone.Permissions.Requests.MayRequest = true;
            c.Everyone.Permissions.Issues.MayReport = true;
            return false;
        });

        _library = InterfaceStub.Of<ILibraryManager>(
            ("GetItemById", a => a[0] is Guid id ? Library.FirstOrDefault(i => i.Id == id) : null),
            // Items read by id together, as a page's are. Any other query finds what Found holds, as the fake library
            // has no database to search.
            ("GetItemList", a => a[0] is InternalItemsQuery { ItemIds.Length: > 0 } query
                ? Library.Where(i => query.ItemIds.Contains(i.Id)).ToList()
                : Found ?? new List<BaseItem>()),
            ("GetCollectionFolders", a => a[0] is BaseItem item && LibraryOf.TryGetValue(item.Id, out var folder)
                ? new List<Folder> { new() { Id = folder } }
                : new List<Folder>()));
        var http = new FakeHttp(this);

        // Jellyfin's items reach the library through a static, as when deciding who may see them.
        BaseItem.LibraryManager = _library;
        Video.RecordingsManager = InterfaceStub.Of<IRecordingsManager>();

        Db = new RequestDatabase(Path.Join(_folder, "requests.db"), NullLogger<RequestDatabase>.Instance);
        var matcher = new LibraryMatcher(_library, InterfaceStub.Of<IUserDataManager>(), Users.Manager);
        Arr = new ArrClient(NullLogger<ArrClient>.Instance, null, http);
        var tmdb = new TmdbDiscovery(http, InterfaceStub.Of<IPluginManager>(), InterfaceStub.Of<IServerConfigurationManager>(), NullLogger<TmdbDiscovery>.Instance);
        Held = new Jellyfin.Plugin.Necto.Services.Notifications.NotificationHeldStore(Path.Join(_folder, "held.json"), null, TimeProvider.System);
        Notifier = new RequestNotifier(Events, NullLogger<RequestNotifier>.Instance, FakeServerConfig.Create(), held: Held);
        Requests = new RequestService(
            Db,
            matcher,
            Arr,
            Notifier,
            Users.Manager,
            Activity.Logger,
            new RequestActivity(Activity.Logger),
            NullLogger<RequestService>.Instance,
            new DetailsService(_library, matcher, tmdb),
            new LibraryRefresher(_library, InterfaceStub.Of<ILibraryMonitor>(), NullLogger<LibraryRefresher>.Instance),
            tmdb);
        var played = InterfaceStub.Of<IUserDataManager>(("GetUserData", a => a[1] is BaseItem item && Played.TryGetValue(item.Id, out var when)
            ? new UserItemData { Key = item.Id.ToString("N"), LastPlayedDate = when }
            : null));
        Issues = new IssueService(Db, _library, played, Arr, Notifier, Activity.Logger, NullLogger<IssueService>.Instance, Clock);
    }

    /// <summary>Gets the Jellyfin messages held for users with no app online.</summary>
    public Jellyfin.Plugin.Necto.Services.Notifications.NotificationHeldStore Held { get; }

    /// <summary>Gets every activity log entry the services wrote, in order.</summary>
    public FakeActivity Activity { get; } = new();

    /// <summary>Gets the clock the issue service reads, which a test moves.</summary>
    public FixedTime Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Gets every event sent to Notifications.</summary>
    public FakeNotifier Events { get; } = new();

    /// <summary>Gets the server's users.</summary>
    public FakeUsers Users { get; } = new();

    public IssueService Issues { get; }

    /// <summary>Gets when each library item was last played, by id, as every user's user data says.</summary>
    public Dictionary<Guid, DateTime> Played { get; } = new();

    /// <summary>Gets or sets what the fake library's searches find, in place of finding nothing.</summary>
    public List<BaseItem>? Found { get; set; }

    public Plugin Plugin { get; }

    public RequestDatabase Db { get; }

    public ArrClient Arr { get; }

    public RequestNotifier Notifier { get; }

    public RequestService Requests { get; }

    /// <summary>Gets the library items GetItemById finds.</summary>
    public List<BaseItem> Library { get; } = new();

    /// <summary>Gets the library (its folder id) each item placed in one is in. No user but an administrator sees
    /// into one, as the fake server's users have no libraries of their own.</summary>
    public Dictionary<Guid, Guid> LibraryOf { get; } = new();

    /// <summary>Gets the movies the fake Radarr holds, by TMDb id.</summary>
    public ConcurrentDictionary<int, JsonObject> RadarrMovies { get; } = new();

    /// <summary>Gets what was posted to Radarr's add and command endpoints, in order.</summary>
    public ConcurrentQueue<(string Path, JsonNode? Body)> RadarrPosts { get; } = new();

    /// <summary>Gets or sets the status the fake Radarr answers every call with, when not 200.</summary>
    public HttpStatusCode? RadarrFails { get; set; }

    /// <summary>
    /// Adds a user. With permissions, the user is the one member of a group of their own that may request and report,
    /// with those permissions on top. Without, Everyone applies.
    /// </summary>
    /// <param name="name">The user's name.</param>
    /// <param name="admin">Whether the user is a server administrator.</param>
    /// <param name="permissions">Changes the group's permissions.</param>
    /// <returns>The user.</returns>
    public User AddUser(string name, bool admin = false, Action<GroupPermissions>? permissions = null)
    {
        var user = Users.Add(name, admin);
        if (permissions is not null)
        {
            var group = TestGroups.Make(name, p =>
            {
                p.Requests.MayRequest = true;
                p.Issues.MayReport = true;
                permissions(p);
            }, user.Id);
            Plugin.MutateConfiguration(c => { c.Groups.Add(group); return false; });
        }

        return user;
    }

    /// <summary>
    /// Gets the API as a user would call it: signed in as them, or with an API key when there is no user, with the
    /// query given.
    /// </summary>
    /// <param name="caller">The user, or null for an API key.</param>
    /// <param name="query">The query string.</param>
    /// <returns>The controller.</returns>
    public RequestsController Api(User? caller, string query = "")
    {
        var claims = caller is null
            ? new[] { new Claim("Jellyfin-IsApiKey", "true") }
            : new[] { new Claim("Jellyfin-UserId", caller.Id.ToString("N")), new Claim("Jellyfin-IsApiKey", "false") };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        context.Request.QueryString = new QueryString(query);
        var host = InterfaceStub.Of<IServerApplicationHost>(("get_SystemId", _ => ServerId));
        var items = new RequestItems(InterfaceStub.Of<IDtoService>(), _library, host);
        return new RequestsController(Requests, Db, null!, null!, Arr, Notifier, null!, items, null!, host, Issues)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    /// <summary>Gets what a user may do, as the services work it out.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The user's access.</returns>
    public UserAccess Access(User user) => Requests.GetAccess(user.Id) ?? throw new InvalidOperationException("No access for " + user.Username);

    /// <summary>Sets up one Radarr with one profile, answered by the fake.</summary>
    /// <param name="more">Further profiles on it.</param>
    public void UseRadarr(params ArrProfileOption[] more)
        => Plugin.MutateConfiguration(c =>
        {
            c.Requests.ArrServers = new List<ArrServer>
            {
                new()
                {
                    Id = "radarr1",
                    Name = "Radarr",
                    Type = RequestType.Movie,
                    Url = RadarrUrl,
                    ApiKey = "key",
                    IsDefault = true,
                    Profiles = new[] { new ArrProfileOption { Id = "hd", Name = "HD", QualityProfileId = 4, RootFolder = "/movies" } }.Concat(more).ToList()
                }
            };
            return false;
        });

    public void Dispose()
    {
        Requests.Dispose();
        Arr.Dispose();
        Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, string? body)
    {
        var uri = request.RequestUri!;
        if (uri.Host != "radarr.test")
        {
            return Json(HttpStatusCode.NotFound, null);
        }

        if (RadarrFails is { } status)
        {
            return Json(status, new JsonObject { ["message"] = "Unauthorized" });
        }

        var path = uri.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (request.Method == HttpMethod.Get && path == "/api/v3/movie/lookup")
        {
            var tmdb = int.Parse((query["term"] ?? string.Empty).Replace("tmdb:", string.Empty, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture);
            var held = RadarrMovies.GetValueOrDefault(tmdb);
            return Json(HttpStatusCode.OK, new JsonArray(held?.DeepClone() ?? new JsonObject { ["title"] = "Movie " + tmdb, ["tmdbId"] = tmdb, ["year"] = 2020, ["images"] = new JsonArray() }));
        }

        if (request.Method == HttpMethod.Get && path == "/api/v3/movie")
        {
            var tmdb = int.TryParse(query["tmdbId"], out var id) ? id : 0;
            return Json(HttpStatusCode.OK, RadarrMovies.TryGetValue(tmdb, out var held) ? new JsonArray(held.DeepClone()) : new JsonArray());
        }

        if (request.Method == HttpMethod.Post && path == "/api/v3/movie")
        {
            var movie = JsonNode.Parse(body!)!.AsObject();
            movie["id"] = 100 + RadarrMovies.Count;
            RadarrMovies[movie["tmdbId"]!.GetValue<int>()] = movie;
            RadarrPosts.Enqueue((path, movie.DeepClone()));
            return Json(HttpStatusCode.Created, movie.DeepClone());
        }

        if (path.StartsWith("/api/v3/movie/", StringComparison.Ordinal) && int.TryParse(path["/api/v3/movie/".Length..], out var arrId))
        {
            var held = RadarrMovies.Values.FirstOrDefault(m => m["id"]?.GetValue<int>() == arrId);
            if (held is null)
            {
                return Json(HttpStatusCode.NotFound, null);
            }

            if (request.Method == HttpMethod.Put)
            {
                var changed = JsonNode.Parse(body!)!.AsObject();
                RadarrMovies[changed["tmdbId"]!.GetValue<int>()] = changed;
                RadarrPosts.Enqueue(("PUT " + path, changed.DeepClone()));
                return Json(HttpStatusCode.Accepted, changed.DeepClone());
            }

            return Json(HttpStatusCode.OK, held.DeepClone());
        }

        if (request.Method == HttpMethod.Post && path == "/api/v3/command")
        {
            RadarrPosts.Enqueue((path, body is null ? null : JsonNode.Parse(body)));
            return Json(HttpStatusCode.Created, new JsonObject { ["id"] = 1 });
        }

        return Json(HttpStatusCode.NotFound, null);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode? body)
        => new(status) { Content = new StringContent(body?.ToJsonString() ?? string.Empty, Encoding.UTF8, "application/json") };

    // Answers every call the services make, as Radarr would.
    private sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
    {
        private readonly RequestHarness _harness;

        public FakeHttp(RequestHarness harness) => _harness = harness;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return _harness.Answer(request, body);
        }
    }
}
