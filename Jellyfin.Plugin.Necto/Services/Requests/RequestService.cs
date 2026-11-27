using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Siphonophore.Stolon;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Who may request, and every change a request goes through: made, approved, declined, removed, sent to
/// Radarr or Sonarr, followed while it downloads, and fulfilled when it reaches the library.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class RequestService : IDisposable
{
    /// <summary>The prefix of every activity log entry type this plugin writes, which Scripts can trigger on.</summary>
    public const string ActivityType = "Requests";

    private const int MaxSeason = 500;

    // The most specials one request asks for, and the highest number one may have.
    private const int MaxSpecials = 100;
    private const int MaxSpecialNumber = 5000;

    // A failed send is retried 15 minutes later, then backing off to at most every six hours.
    private static readonly BackoffPolicy _sendBackoff = new(1, TimeSpan.FromMinutes(15), TimeSpan.FromHours(6));

    // Counts requests made, for Version.
    private long _version;

    // Requests that were available and lost their title, by when: one back within a few hours (Radarr or Sonarr
    // replacing a file with a better copy, which Jellyfin sees as removed and added again) isn't announced twice.
    private static readonly TimeSpan ReturnQuietly = TimeSpan.FromHours(6);
    private readonly ConcurrentDictionary<Guid, DateTime> _lostAvailable = new();

    private readonly RequestDatabase _db;
    private readonly LibraryMatcher _library;
    private readonly ArrClient _arr;
    private readonly DetailsService _details;
    private readonly TmdbDiscovery _tmdb;

    // How many older requests' genres are filled in on each hourly run.
    private const int GenreFillBatch = 50;

    // How long one run of the hourly task spends filling in genres.
    private static readonly TimeSpan GenreFillTime = TimeSpan.FromSeconds(20);

    private readonly RequestNotifier _notifier;
    private readonly IUserManager _userManager;
    private readonly ActivityLogger _activity;
    private readonly RequestActivity _summary;
    private readonly ILogger<RequestService> _logger;
    private readonly LibraryRefresher _refresher;

    // How many files each open request had when last checked, so Jellyfin is only told about new ones. The first sync
    // after a start only takes note: what was already there before then isn't new, and the library's own scan has it.
    private readonly ConcurrentDictionary<Guid, int> _filesSeen = new();
    private bool _filesPrimed;
    private readonly ConcurrentDictionary<Guid, byte> _sending = new();

    // One status sync at a time: the scheduled task and an administrator's refresh would otherwise both resend the
    // same failed requests and both refresh the library for the same new files.
    private readonly SemaphoreSlim _syncing = new(1, 1);

    // Approvals sent at once in the background, and those waiting their turn.
    private readonly SemaphoreSlim _sendGate = new(4, 4);
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestService"/> class.
    /// </summary>
    /// <param name="db">The request database.</param>
    /// <param name="library">The library matcher.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="notifier">The notifier.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="activity">The activity log writer.</param>
    /// <param name="summary">The activity summaries, for entries that come in bursts.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="details">The details service, for what a title is when Radarr and Sonarr can't say.</param>
    /// <param name="refresher">The library refresher, told about files Radarr or Sonarr has just put in place.</param>
    /// <param name="tmdb">TMDb, through Jellyfin's access, for a request's genres.</param>
    public RequestService(
        RequestDatabase db,
        LibraryMatcher library,
        ArrClient arr,
        RequestNotifier notifier,
        IUserManager userManager,
        ActivityLogger activity,
        RequestActivity summary,
        ILogger<RequestService> logger,
        DetailsService details,
        LibraryRefresher refresher,
        TmdbDiscovery tmdb)
    {
        _db = db;
        _tmdb = tmdb;
        _library = library;
        _arr = arr;
        _details = details;
        _notifier = notifier;
        _userManager = userManager;
        _activity = activity;
        _summary = summary;
        _logger = logger;
        _refresher = refresher;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _syncing.Dispose();
        _sendGate.Dispose();
    }

    // ------------------------------------------------------------------ access

    /// <summary>Gets a value indicating whether Requests is on.</summary>
    public static bool Enabled => Jellyfin.Plugin.Necto.Configuration.ToolSwitches.IsOn(Jellyfin.Plugin.Necto.Configuration.ToolSwitches.Requests);

    /// <summary>
    /// Works out what a user may do, or returns null for an unknown or disabled account.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The user's access, or null.</returns>
    public UserAccess? GetAccess(Guid userId)
    {
        var user = userId == Guid.Empty ? null : _userManager.GetUserById(userId);
        var all = RequestConfig.Instance?.ReadAll(c => c);
        var config = all?.Requests;
        if (user is null || all is null || config is null || user.HasPermission(PermissionKind.IsDisabled))
        {
            return null;
        }

        var isAdmin = user.HasPermission(PermissionKind.IsAdministrator);
        var permissions = PermissionRules.For(all, user.Id, isAdmin);
        return new UserAccess
        {
            User = user,
            UserId = user.Id,
            UserName = user.Username,
            IsAdministrator = isAdmin || permissions.ManageRequests,
            IsServerAdministrator = isAdmin,
            ManagesIssues = isAdmin || permissions.ManageIssues,
            Permissions = permissions,
            RequestsEnabled = config.Enabled,
            CanRequest = config.Enabled && permissions.CanRequest,
            AutoApproveMovies = permissions.AutoApproveMovies,
            AutoApproveShows = permissions.AutoApproveShows,
            SeeAllRequests = permissions.SeeAllRequests,
            SeeRequesters = permissions.SeeRequesters,
            Movies = permissions.Movies,
            Series = permissions.Shows,
            MaxOpenRequests = permissions.MaxOpenRequests,
            MaxSeasonsPerShow = permissions.MaxSeasons,
            AllowSpecials = config.AllowSpecials,
            IssuesEnabled = all.Issues?.Enabled == true,
            ReportIssues = all.Issues?.Enabled == true && permissions.ReportIssues,
            MovieProfiles = ProfileOptions(config, RequestType.Movie, permissions, isAdmin),
            ShowProfiles = ProfileOptions(config, RequestType.Series, permissions, isAdmin)
        }.CountedWhenRead(
            () => _db.CountHoldingAPlace(user.Id),
            () => Quota(user.Id, RequestType.Movie, permissions.MovieQuotaLimit, permissions.MovieQuotaDays),
            () => ShowQuota(user.Id, permissions.ShowQuotaLimit, permissions.ShowQuotaDays, null));
    }

    /// <summary>
    /// Lists the profiles a user picks from for a type, the default server's first and the rest in their servers'
    /// order: the ones they're allowed, or every one when none are set and for administrators. Empty only when no
    /// Radarr or Sonarr is set up for the type.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="type">The type.</param>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <returns>The profiles.</returns>
    internal static List<ProfileOption> ProfileOptions(RequestSettings config, RequestType type, RequestGrant permissions, bool isAdmin)
    {
        var usable = ArrServers.Usable(config, type).Select(u => u.Profile).ToList();
        var allowed = type == RequestType.Movie ? permissions.MovieProfiles : permissions.ShowProfiles;
        var profiles = isAdmin || allowed.Count == 0 ? usable : usable.Where(p => allowed.Contains(p.Id, StringComparer.Ordinal)).ToList();

        // Allowed profiles that have all been removed since leave the default server's first, never every one.
        if (profiles.Count == 0)
        {
            profiles = usable.Take(1).ToList();
        }

        // Two servers may each have a "4K"; requesters see names only, so repeats are numbered rather than named by server.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return profiles.Select(p =>
        {
            var name = string.IsNullOrWhiteSpace(p.Name) ? "Default" : p.Name.Trim();
            var count = seen[name] = seen.GetValueOrDefault(name) + 1;
            return new ProfileOption(p.Id, count == 1 ? name : name + " (" + count.ToString(CultureInfo.InvariantCulture) + ")");
        }).ToList();
    }

    // ------------------------------------------------------------------ seasons

    /// <summary>
    /// Lists a show's seasons with what the library and the open requests already cover, for the request form.
    /// </summary>
    /// <param name="show">The show.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="viewer">Who is asking, so a show they may not see in the library is not shown as there.</param>
    /// <returns>The seasons.</returns>
    public async Task<SeriesDetails> GetSeriesDetailsAsync(SeriesLookup show, CancellationToken cancellationToken, UserAccess? viewer = null)
    {
        ArgumentNullException.ThrowIfNull(show);
        var ids = ProviderIds.Clean(show.ProviderIds);
        // TMDb is asked first; the library and the requests are read meanwhile.
        var asking = _tmdb.SeasonsAsync(ids, cancellationToken);
        var series = viewer?.User is { } user
            ? _library.Find(RequestType.Series, ids, show.Title, show.Year, user)
            : _library.Find(RequestType.Series, ids, show.Title, show.Year);
        var open = _db.OpenRequests().Where(r => r.HoldsAPlace && ProviderIds.Matches(r, RequestType.Series, ids)).ToList();
        var requested = open.Where(r => r.Seasons is not null && r.Specials is null).SelectMany(r => r.Seasons!).ToHashSet();
        var config = RequestConfig.Instance?.ReadConfiguration(c => (c.FullSeriesOnly, c.AllowSpecials)) ?? (false, false);
        // The show's specials, where they can be requested: asked of TMDb meanwhile too.
        var askingSpecials = config.AllowSpecials ? _tmdb.EpisodesAsync(ids, 0, cancellationToken) : Task.FromResult<SeasonEpisodes?>(null);

        // A season counts as in the library once it has every episode that has aired.
        var seasons = await asking.ConfigureAwait(false);
        var inLibrary = series is null ? new List<int>() : _library.SeasonsInLibrary(series, Expected(seasons));
        var details = new SeriesDetails
        {
            CanPickSeasons = seasons is { Count: > 0 } && !config.FullSeriesOnly,
            WholeShowOnly = config.FullSeriesOnly && seasons is { Count: > 0 },
            LibraryItemId = series?.Id,
            WholeShowRequested = open.Any(r => r.Seasons is null)
        };
        if ((await askingSpecials.ConfigureAwait(false))?.Episodes is { Count: > 0 } specials)
        {
            var counts = series is null ? null : _library.CountEpisodes(series);
            var asked = open.Where(r => r.Specials is not null).SelectMany(r => r.Specials!).Select(x => x.Number).ToHashSet();
            details.Specials = specials.Select(e => new SpecialEpisode { Number = e.Number, Name = e.Name, AirDate = e.AirDate })
                .Select(x =>
                {
                    x.InLibrary = counts?.HasSpecial(x) == true;
                    x.Requested = asked.Contains(x.Number);
                    return x;
                })
                .ToList();
        }

        // Without TMDb the library's own seasons are still shown, so the requester can see what is there.
        details.Seasons = (seasons ?? inLibrary.Select(n => new SeasonInfo { Number = n }).ToList())
            .Select(s =>
            {
                s.InLibrary = inLibrary.Contains(s.Number);
                s.Requested = details.WholeShowRequested || requested.Contains(s.Number);
                return s;
            })
            .ToList();
        return details;
    }

    // ------------------------------------------------------------------ requesting

    /// <summary>
    /// Makes a request, approving it straight away for administrators and auto-approved users.
    /// </summary>
    /// <param name="access">The requester's access.</param>
    /// <param name="submit">What they picked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="sendWait">How long one approved straight away waits for its send; a second when not given.</param>
    /// <returns>The new request, or an error to show them.</returns>
    public async Task<(MediaRequest? Request, string? Error)> SubmitAsync(UserAccess access, NewMediaRequest submit, CancellationToken cancellationToken, TimeSpan? sendWait = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(submit);

        if (!access.CanRequest)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorNotAllowedToRequest));
        }

        if (!Enum.IsDefined(submit.Type))
        {
            return (null, Say(access, TranslationKeys.RequestsErrorPickFromResults));
        }

        if ((submit.Type == RequestType.Movie && !access.Movies) || (submit.Type == RequestType.Series && !access.Series))
        {
            return (null, submit.Type == RequestType.Movie ? Say(access, TranslationKeys.RequestsErrorMoviesNotRequestable) : Say(access, TranslationKeys.RequestsErrorShowsNotRequestable));
        }

        var ids = ProviderIds.Clean(submit.ProviderIds);
        var title = (submit.Title ?? string.Empty).Trim();
        if (ids.Count == 0 || title.Length == 0 || title.Length > 300 || title.Any(char.IsControl))
        {
            return (null, Say(access, TranslationKeys.RequestsErrorPickFromResults));
        }

        var seasons = submit.Type == RequestType.Series ? NormalizeSeasons(submit.Seasons) : null;
        if (submit.Type == RequestType.Series && submit.Seasons is { Count: > 0 } && seasons is null)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorSeasonsInvalid));
        }

        var config = RequestConfig.Instance!.ReadConfiguration(c => c);

        // Specials are a request of their own, each one picked: never with seasons, and only where the server allows them.
        List<SpecialEpisode>? specials = null;
        if (submit.Type == RequestType.Series && submit.Specials is { Count: > 0 } picked0)
        {
            if (!config.AllowSpecials)
            {
                return (null, Say(access, TranslationKeys.RequestsErrorSpecialsNotRequestable));
            }

            if (seasons is not null || picked0.Count > MaxSpecials || picked0.Any(n => n < 1 || n > MaxSpecialNumber))
            {
                return (null, Say(access, TranslationKeys.RequestsErrorSpecialsInvalid));
            }

            // As TMDb lists them, so each is kept with its name and the day it aired.
            if ((await _tmdb.EpisodesAsync(ids, 0, cancellationToken).ConfigureAwait(false))?.Episodes is not { } listed)
            {
                return (null, Say(access, TranslationKeys.RequestsErrorSpecialsUnread));
            }

            specials = picked0.Distinct().Order()
                .Select(n => listed.FirstOrDefault(e => e.Number == n))
                .Select(e => e is null ? null : new SpecialEpisode { Number = e.Number, Name = Truncate(e.Name, 200), AirDate = e.AirDate })
                .OfType<SpecialEpisode>()
                .ToList();
            if (specials.Count != picked0.Distinct().Count())
            {
                return (null, Say(access, TranslationKeys.RequestsErrorSpecialsInvalid));
            }

            seasons = new List<int> { 0 };
        }
        else if (submit.Type == RequestType.Series && config.FullSeriesOnly && seasons is not null)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorWholeShowsOnly));
        }

        // The title, its artwork and every id it has, from TMDb by its ids, so what an administrator approves is what
        // gets downloaded, and a request sent with only some of a title's ids (a show without its TVDB id, which Sonarr
        // knows shows by) is checked against the blocklist and the open requests by all of them, and stored with them.
        var described = await DescribeAsync(submit.Type, ids, cancellationToken).ConfigureAwait(false);
        if (described is not null)
        {
            title = described.Title;
            ids = described.ProviderIds;
        }

        // A title TMDb can't describe is known only by the ids and name the requester sent, which the blocked titles and
        // words can't be trusted to match (another of its ids may be the blocked one), so with either set it's refused
        // rather than let through unchecked.
        if (described is null && !access.IsAdministrator && _tmdb.Available
            && (config.BlockedTitles is { Count: > 0 } || config.BlockedKeywords is { Count: > 0 }))
        {
            _logger.LogWarning("Refused a request from {User}: TMDb could not describe the title to check it against the blocklist", access.UserName);
            return (null, Say(access, CouldNotCheck));
        }

        // The blocklist and release rule, which administrators are not held to. Genres, rating and release come
        // from TMDb, else from the library.
        var facts = await FactsAsync(submit.Type, ids, title, submit.Year, described, cancellationToken).ConfigureAwait(false);

        if (RuleRefusal(config, access, submit.Type, ids, title, facts) is { } refusal)
        {
            if (!facts.Known && refusal == CouldNotCheck)
            {
                _logger.LogWarning("Refused a request for {Title} from {User}: its details could not be read to check the request rules", title, access.UserName);
            }

            return (null, Say(access, refusal));
        }

        // What the library already has decides whether there is anything left to request. A title the requester
        // may not see in the library is refused as a blocked one is, so the answer doesn't say it is there.
        // The copy the requester can see first, so a hidden copy elsewhere (an administrators' 4K library, say) isn't used.
        var libraryItem = (access.User is null ? null : _library.Find(submit.Type, ids, title, submit.Year, user: access.User))
            ?? _library.Find(submit.Type, ids, title, submit.Year);
        if (libraryItem is not null && access.User is not null && !LibraryMatcher.IsVisibleTo(libraryItem, access.User))
        {
            return (null, Say(access, TranslationKeys.RequestsErrorTitleNotRequestable));
        }

        if (libraryItem is not null && specials is not null)
        {
            var counts = _library.CountEpisodes(libraryItem);
            specials = specials.Where(x => !counts.HasSpecial(x)).ToList();
            if (specials.Count == 0)
            {
                return (null, Say(access, TranslationKeys.RequestsErrorHasSpecials));
            }
        }
        else if (libraryItem is not null)
        {
            // With only whole shows to request, one the library has part of is asked for every season it's missing.
            if (submit.Type == RequestType.Series && seasons is null && config.FullSeriesOnly
                && described?.ExpectedEpisodes?.Keys.Where(k => k > 0).Order().ToList() is { Count: > 0 } every)
            {
                seasons = every;
            }

            if (submit.Type == RequestType.Movie || seasons is null)
            {
                return (null, submit.Type == RequestType.Movie ? Say(access, TranslationKeys.RequestsErrorHasTitle) : Say(access, TranslationKeys.RequestsErrorHasShowPickSeasons));
            }

            var have = _library.SeasonsInLibrary(libraryItem, described?.ExpectedEpisodes);
            seasons = seasons.Except(have).ToList();
            if (seasons.Count == 0)
            {
                return (null, config.FullSeriesOnly ? Say(access, TranslationKeys.RequestsErrorHasShow) : Say(access, TranslationKeys.RequestsErrorHasSeasons));
            }
        }

        // A title a Radarr or Sonarr is already looking after (a monitored movie, or every season still wanted monitored
        // there) is on its way, so it can't be requested, by administrators either. One it holds but has let go of can,
        // and sending it switches it back on. One that couldn't be asked can't be checked, so the request waits until it can.
        // Specials aren't asked about: Sonarr looks after them one by one, which sending them works out.
        switch (specials is not null ? false : await HasAsync(submit.Type, title, submit.Year, ids, seasons, cancellationToken).ConfigureAwait(false))
        {
            case true:
                return (null, Say(access, TranslationKeys.RequestsErrorAlreadyWaiting));
            case null:
                _logger.LogWarning("Refused a request for {Title} from {User}: Radarr or Sonarr could not be asked whether it has it", title, access.UserName);
                return (null, Say(access, CouldNotCheck));
        }

        var seasonCount = seasons?.Count ?? described?.ExpectedEpisodes?.Keys.Count(k => k > 0);

        // A whole show whose seasons can't be counted would slip past a season limit, so with one it's refused.
        if (submit.Type == RequestType.Series && seasonCount is null && access.MaxSeasonsPerShow > 0)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorSeasonsUncounted));
        }

        // The profile picked, from the ones the requester sees; the first of them (the default server's, when they may
        // pick it) when none was sent.
        var permissions = access.Permissions ?? PermissionRules.For(config, access.UserId, access.IsAdministrator);
        var options = ProfileOptions(config, submit.Type, permissions, access.IsAdministrator);
        var picked = (submit.ProfileId ?? string.Empty).Trim();
        if (picked.Length > 0 && !options.Any(o => o.Id == picked))
        {
            return (null, Say(access, TranslationKeys.RequestsErrorProfileNotAllowed));
        }

        var target = options.Count == 0 ? null : ArrServers.Resolve(config, submit.Type, picked.Length > 0 ? picked : options[0].Id);

        // One each: a movie, or a show however many of its seasons.
        const int cost = 1;
        var wanted = seasons ?? described?.ExpectedEpisodes?.Keys.Where(k => k > 0).ToList();
        var now = DateTime.UtcNow;
        var (request, error) = _db.Atomic<(MediaRequest?, string?)>(() =>
        {
            // Only open requests count: what earlier requests brought in is the library check's business, and a
            // fulfilled whole-show request must not block a later season. The kept list is exact here: nothing has
            // been written in this Atomic yet, and no one else can write while it runs.
            var existing = Duplicate(_db.OpenRequests(), submit.Type, ids, seasons, specials);
            if (existing is not null)
            {
                return (null, existing.UserId == access.UserId ? Say(access, TranslationKeys.RequestsErrorYouRequested)
                    : (access.IsAdministrator || access.SeeRequesters) && existing.UserName.Length > 0 ? existing.UserName + " already requested this."
                    : Say(access, TranslationKeys.RequestsErrorSomeoneRequested));
            }

            if (access.MaxOpenRequests > 0 && _db.CountHoldingAPlace(access.UserId) >= access.MaxOpenRequests)
            {
                return (null, StolonTranslations.Format(TranslationKeys.RequestsErrorOpenLimit, access.Language, access.MaxOpenRequests));
            }

            if (submit.Type == RequestType.Series && SeasonCapRefusal(access, ids, wanted, null) is { } capped)
            {
                return (null, capped);
            }

            // Movies count one each; shows one each, however many seasons, and a show already counted in the window
            // not again.
            var quota = submit.Type == RequestType.Movie
                ? Quota(access.UserId, RequestType.Movie, permissions.MovieQuotaLimit, permissions.MovieQuotaDays, cost)
                : ShowQuota(access.UserId, permissions.ShowQuotaLimit, permissions.ShowQuotaDays, ids);
            if (quota is not null && quota.Need > 0 && quota.Used + quota.Need > quota.Limit)
            {
                return (null, QuotaMessage(submit.Type, quota, access.Language));
            }

            var created = new MediaRequest
            {
                Id = Guid.NewGuid(),
                Type = submit.Type,
                Title = title,
                Year = described?.Year ?? (submit.Year is > 1800 and < 3000 ? submit.Year : null),
                Overview = Truncate(described?.Overview ?? submit.Overview, 2000),
                PosterUrl = described is null ? SafePosterUrl(submit.PosterUrl) : described.PosterUrl,
                BackdropUrl = described is null ? SafeBackdropUrl(submit.BackdropUrl) : described.BackdropUrl,
                ProviderIds = ids,
                Seasons = seasons,
                Specials = specials,
                ExpectedEpisodes = described?.ExpectedEpisodes,
                UserId = access.UserId,
                UserName = access.UserName,
                RequesterNote = Truncate(submit.Note, 500),
                CreatedUtc = now,
                UpdatedUtc = now,
                Status = RequestStatus.Pending,

                // The requester's profile on the server it belongs to; an administrator may switch it on approval.
                ServerId = target?.Server.Id,
                ProfileId = target?.Profile.Id,
                QuotaCost = cost
            };
            _db.Insert(created);
            Interlocked.Increment(ref _version);
            return (created, null);
        });

        if (request is null)
        {
            return (null, error);
        }

        _activity.Log(
            ServerWording.Get(TranslationKeys.RequestsActivityCreatedName),
            ActivityType + ".Created",
            ActivityText.Sentence(ServerWording.Format(TranslationKeys.RequestsActivityCreated, access.UserName, Describe(request))),
            userId: access.UserId);

        // Its genres as TMDb names them, the names the page filters by, filled in once it's made: making a request
        // never waits on TMDb. If TMDb doesn't answer, the hourly task fills them in.
        _ = FillGenresOfAsync(request.Id, request.Type, request.ProviderIds);

        // Approved straight away for administrators and users trusted with the type, and for anyone when the
        // title's age rating is on the auto-approve list. The request exists now, so a requester going away mid-way
        // doesn't leave it waiting unannounced.
        var trusted = submit.Type == RequestType.Movie ? access.AutoApproveMovies : access.AutoApproveShows;
        var byRating = facts.Rating is { } rating && config.AutoApproveRatings.Contains(rating, StringComparer.OrdinalIgnoreCase);
        if (trusted || byRating)
        {
            request = await ApproveAsync(request.Id, trusted ? access.UserName : "Auto-approved (" + facts.Rating + ")", CancellationToken.None, notify: false, waitFor: sendWait).ConfigureAwait(false) ?? request;
        }

        _notifier.Notify(NotificationEvent.Created, request);

        // Approved by its rating, the requester hears it was approved as from an administrator. Approved by their own
        // trust, they made the decision themselves, so there's nothing to tell them (the notifier skips a requester
        // who decided their own request in any case). Sent after the new request, so it reads in order.
        if (!trusted && byRating && request.Status == RequestStatus.Approved)
        {
            _notifier.Notify(NotificationEvent.Approved, request);
        }

        return (request, null);
    }

    /// <summary>
    /// Changes a request still waiting for approval: its seasons, profile and note, held to what a new request is. Its
    /// requester may, and administrators may change anyone's, as they're held to no limits.
    /// </summary>
    /// <param name="access">The access of whoever changes it.</param>
    /// <param name="id">The request id.</param>
    /// <param name="edit">What they picked.</param>
    /// <returns>The changed request and no error; an error to show them; or neither when there's no such request of
    /// theirs still waiting.</returns>
    public (MediaRequest? Request, string? Error) Edit(UserAccess access, Guid id, MediaRequestEdit edit)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(edit);

        var current = _db.Get(id);
        if (current is null || !MayEdit(current, access))
        {
            return (null, null);
        }

        if (!access.CanRequest)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorNotAllowedToRequest));
        }

        if (!access.IsAdministrator && !(current.Type == RequestType.Movie ? access.Movies : access.Series))
        {
            return (null, current.Type == RequestType.Movie ? Say(access, TranslationKeys.RequestsErrorMoviesNotRequestable) : Say(access, TranslationKeys.RequestsErrorShowsNotRequestable));
        }

        var seasons = current.Type == RequestType.Series ? NormalizeSeasons(edit.Seasons) : null;
        if (current.Type == RequestType.Series && edit.Seasons is { Count: > 0 } && seasons is null)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorSeasonsInvalid));
        }

        // What's asked for stays as it was for a request for specials, and where only whole shows can be requested:
        // its note and profile can still change.
        var keepsSeasons = current.Specials is not null || RequestConfig.Instance!.ReadConfiguration(c => c.FullSeriesOnly);
        if (keepsSeasons)
        {
            seasons = current.Seasons;
        }

        // Seasons the library has since gained can't be asked for again, and a show in it needs seasons named.
        if (current.Type == RequestType.Series && !keepsSeasons
            && ((access.User is null ? null : _library.Find(current.Type, current.ProviderIds, current.Title, current.Year, user: access.User))
                ?? _library.Find(current.Type, current.ProviderIds, current.Title, current.Year)) is { } libraryItem)
        {
            // As when it was made: a copy the requester can't see isn't said to be there.
            if (access.User is not null && !LibraryMatcher.IsVisibleTo(libraryItem, access.User))
            {
                return (null, Say(access, TranslationKeys.RequestsErrorTitleNotRequestable));
            }

            if (seasons is null)
            {
                return (null, Say(access, TranslationKeys.RequestsErrorHasShowPickSeasons));
            }

            seasons = seasons.Except(_library.SeasonsInLibrary(libraryItem, current.ExpectedEpisodes)).ToList();
            if (seasons.Count == 0)
            {
                return (null, Say(access, TranslationKeys.RequestsErrorHasSeasons));
            }
        }

        var seasonCount = seasons?.Count ?? current.ExpectedEpisodes?.Keys.Count(k => k > 0);
        if (current.Type == RequestType.Series && seasonCount is null && access.MaxSeasonsPerShow > 0)
        {
            return (null, Say(access, TranslationKeys.RequestsErrorSeasonsUncounted));
        }

        var config = RequestConfig.Instance!.ReadConfiguration(c => c);
        var permissions = access.Permissions ?? PermissionRules.For(config, access.UserId, access.IsAdministrator);
        var options = ProfileOptions(config, current.Type, permissions, access.IsAdministrator);
        var picked = (edit.ProfileId ?? string.Empty).Trim();
        if (picked.Length > 0 && !options.Any(o => o.Id == picked))
        {
            return (null, Say(access, TranslationKeys.RequestsErrorProfileNotAllowed));
        }

        var target = options.Count == 0 ? null : ArrServers.Resolve(config, current.Type, picked.Length > 0 ? picked : options[0].Id);
        const int cost = 1;
        var wanted = seasons ?? current.ExpectedEpisodes?.Keys.Where(k => k > 0).ToList();
        var (changed, error) = _db.Atomic<(MediaRequest?, string?)>(() =>
        {
            var r = _db.Get(id);
            if (r is null || !MayEdit(r, access))
            {
                return (null, null);
            }

            // Someone else's open request for the same seasons, or more than the quota allows, leaves it as it was.
            var others = _db.OpenRequests().Where(o => o.Id != r.Id);
            if (r.Type == RequestType.Series && Duplicate(others, r.Type, r.ProviderIds, seasons, r.Specials) is { } existing)
            {
                return (null, existing.UserId == access.UserId ? Say(access, TranslationKeys.RequestsErrorYouRequested)
                    : (access.IsAdministrator || access.SeeRequesters) && existing.UserName.Length > 0 ? existing.UserName + " already requested this."
                    : Say(access, TranslationKeys.RequestsErrorSomeoneRequested));
            }

            if (r.Type == RequestType.Series && SeasonCapRefusal(access, r.ProviderIds, wanted, r.Id) is { } capped)
            {
                return (null, capped);
            }

            // Changing a request never costs more: it's one movie or one show either way.
            var quota = r.Type == RequestType.Movie
                ? Quota(access.UserId, RequestType.Movie, permissions.MovieQuotaLimit, permissions.MovieQuotaDays, cost, r.Id)
                : ShowQuota(access.UserId, permissions.ShowQuotaLimit, permissions.ShowQuotaDays, r.ProviderIds, r.Id);
            if (quota is not null && quota.Need > 0 && quota.Used + quota.Need > quota.Limit)
            {
                return (null, QuotaMessage(r.Type, quota, access.Language));
            }

            r.Seasons = seasons;
            r.RequesterNote = Truncate(edit.Note, 500);
            r.ServerId = target?.Server.Id;
            r.ProfileId = target?.Profile.Id;
            r.QuotaCost = cost;
            r.UpdatedUtc = DateTime.UtcNow;
            _db.Update(r);
            return (r, null);
        });

        if (changed is null)
        {
            return (null, error);
        }

        Interlocked.Increment(ref _version);
        _activity.Log(
            ServerWording.Get(TranslationKeys.RequestsActivityChangedName),
            ActivityType + ".Changed",
            ActivityText.Sentence(changed.UserId == access.UserId
                ? ServerWording.Format(TranslationKeys.RequestsActivityChangedOwn, access.UserName, Describe(changed))
                : ServerWording.Format(TranslationKeys.RequestsActivityChangedOther, access.UserName, Describe(changed), changed.UserName)),
            userId: changed.UserId);
        return (changed, null);
    }

    /// <summary>
    /// Joins someone else's open request for a title the caller wants too, or leaves it: they're told about it as its
    /// requester is, it shows among their requests, and it costs them nothing.
    /// </summary>
    /// <param name="access">Who joins or leaves.</param>
    /// <param name="id">The request id.</param>
    /// <param name="join">True to join, false to leave.</param>
    /// <returns>The request and no error; an error to show them; or neither when there's no such open request.</returns>
    public (MediaRequest? Request, string? Error) Join(UserAccess access, Guid id, bool join)
    {
        ArgumentNullException.ThrowIfNull(access);
        return _db.Atomic<(MediaRequest?, string?)>(() =>
        {
            // Joining needs a request still holding a place; leaving one is always allowed, however it ended.
            var r = _db.Get(id);
            if (r is null || (join && !r.HoldsAPlace) || access.UserId == Guid.Empty)
            {
                return (null, null);
            }

            var joined = r.Joined ?? new List<RequestJoiner>();
            var already = joined.Any(j => j.UserId == access.UserId);
            if (join)
            {
                if (r.UserId == access.UserId)
                {
                    return (null, Say(access, TranslationKeys.RequestsErrorOwnRequest));
                }

                if (!access.CanRequest || !(r.Type == RequestType.Movie ? access.Movies : access.Series))
                {
                    return (null, r.Type == RequestType.Movie ? Say(access, TranslationKeys.RequestsErrorMoviesNotRequestable) : Say(access, TranslationKeys.RequestsErrorShowsNotRequestable));
                }

                // One partly in a library hidden from them can't be joined: following it would show what that library holds.
                if (!already && r.LibraryItemId is { } itemId && !CanSee(itemId, access))
                {
                    return (null, null);
                }

                if (already)
                {
                    return (r, null);
                }

                joined.Add(new RequestJoiner { UserId = access.UserId, UserName = access.UserName, JoinedUtc = DateTime.UtcNow });
            }
            else
            {
                if (!already)
                {
                    return (r, null);
                }

                joined.RemoveAll(j => j.UserId == access.UserId);
            }

            r.Joined = joined.Count > 0 ? joined : null;
            _db.Update(r);
            return (r, null);
        });
    }

    /// <summary>
    /// Fills in the genres of requests made before they were kept, from TMDb, a few at a time so the hourly task stays
    /// quick; the rest follow on later runs. One TMDb doesn't know is marked as having none, so it isn't asked again.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many were filled in.</returns>
    public async Task<int> FillGenresAsync(CancellationToken cancellationToken)
    {
        if (!_tmdb.Available)
        {
            return 0;
        }

        // At most GenreFillTime a run, so a slow TMDb never holds up the rest of the task; the rest follow next run.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(GenreFillTime);
        var filled = 0;
        try
        {
            foreach (var request in _db.WithoutGenres(GenreFillBatch))
            {
                // TMDb not answering leaves the rest for the next run, rather than waiting on it for each. A title TMDb
                // doesn't know is marked as having none.
                if (await _tmdb.GenresOfAsync(request.Type, request.ProviderIds, budget.Token).ConfigureAwait(false) is not { } genres)
                {
                    break;
                }

                if (Change(request.Id, r => r.Genres is null, r => r.Genres = genres, touch: false) is not null)
                {
                    filled++;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Out of time for this run.
        }

        return filled;
    }

    // Fills in a new request's genres from TMDb, in the background; left for the hourly task when TMDb doesn't answer.
    private async Task FillGenresOfAsync(Guid requestId, RequestType type, Dictionary<string, string> ids)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (await _tmdb.GenresOfAsync(type, ids, timeout.Token).ConfigureAwait(false) is { } genres)
            {
                Change(requestId, r => r.Genres is null, r => r.Genres = genres, touch: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Left for the hourly task.
        }
        catch (Exception ex)
        {
            // Nothing waits on this: the hourly task fills in what's missed.
            _logger.LogDebug(ex, "Filling in a new request's genres failed");
        }
    }

    /// <summary>Whether someone may change a request: their own, or anyone's for an administrator, while it waits.</summary>
    /// <param name="request">The request.</param>
    /// <param name="access">Their access.</param>
    /// <returns>True when they may.</returns>
    internal static bool MayEdit(MediaRequest request, UserAccess access)
        => request.Status == RequestStatus.Pending && (request.UserId == access.UserId || access.IsAdministrator);

    // ------------------------------------------------------------------ deciding

    /// <summary>
    /// Approves a pending or failed request and sends it to Radarr or Sonarr when one is set up.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="approver">The administrator's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="notify">Whether to send the approval as an update; a request approved as it is made is sent as a new request instead.</param>
    /// <param name="profileId">A profile to send it with instead of the one it has, on whichever server holds it.</param>
    /// <param name="waitFor">How long to wait for the send before answering; a second when not given.</param>
    /// <param name="logActivity">Whether to write its activity entry; a bulk action writes one for all instead.</param>
    /// <returns>The updated request, or null when there is no such pending or failed request.</returns>
    public async Task<MediaRequest?> ApproveAsync(Guid id, string approver, CancellationToken cancellationToken, bool notify = true, string? profileId = null, TimeSpan? waitFor = null, bool logActivity = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = RequestConfig.Instance?.ReadConfiguration(c => c) ?? new RequestSettings();
        var request = Change(id, r => r.Status is RequestStatus.Pending or RequestStatus.Failed, r =>
        {
            r.Status = RequestStatus.Approved;
            r.DecidedBy = approver;
            r.DecidedUtc = DateTime.UtcNow;
            r.Note = null;

            // Approved again by hand, a failed send starts afresh: its next failure is reported, and it backs off from the start.
            r.SendFailures = 0;
            r.RetryAfterUtc = null;

            // A request not yet in Radarr or Sonarr can move to another profile, and with it another server.
            if (!string.IsNullOrEmpty(profileId) && r.ArrId is null
                && ArrServers.Usable(config, r.Type).FirstOrDefault(u => string.Equals(u.Profile.Id, profileId, StringComparison.Ordinal)) is { Server: not null } chosen)
            {
                r.ServerId = chosen.Server.Id;
                r.ProfileId = chosen.Profile.Id;
            }
        });
        if (request is null)
        {
            return null;
        }

        if (logActivity)
        {
            _activity.Log(
                ServerWording.Get(TranslationKeys.RequestsActivityApprovedName),
                ActivityType + ".Approved",
                ActivityText.Sentence(string.Equals(approver, request.UserName, StringComparison.Ordinal)
                    ? ServerWording.Format(TranslationKeys.RequestsActivityApprovedOwn, approver, Describe(request))
                    : ServerWording.Format(TranslationKeys.RequestsActivityApproved, approver, Describe(request), request.UserName)),
                userId: request.UserId);
        }

        // Sent on its own, so it finishes even if the administrator's page goes away mid-way. The answer waits a moment
        // for it: a server that answers quickly is reported as it went, and a slow one finishes in the background (one
        // cut short is sent again by the status sync).
        var sending = SendThenTellAsync(request, notify);
        if (await Task.WhenAny(sending, Task.Delay(waitFor ?? SendWait, CancellationToken.None)).ConfigureAwait(false) == sending)
        {
            return await sending.ConfigureAwait(false);
        }

        // Left to finish on its own, so a failure is logged here; nothing else awaits it.
        _ = sending.ContinueWith(
            t => _logger.LogError(t.Exception, "Sending the approved request {Id} failed", request.Id),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return _db.Get(request.Id) ?? request;
    }

    // How long an approval waits for its send before answering.
    private static readonly TimeSpan SendWait = TimeSpan.FromSeconds(1);

    private async Task<MediaRequest> SendThenTellAsync(MediaRequest request, bool notify)
    {
        // A few at a time, so a bulk approval doesn't send hundreds at once. One waiting its turn is queued, so the
        // status sync doesn't send it as well.
        _queued.TryAdd(request.Id, 0);
        try
        {
            try
            {
                await _sendGate.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return request;
            }

            try
            {
                request = await SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    _sendGate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Shutting down.
                }
            }
        }
        finally
        {
            _queued.TryRemove(request.Id, out _);
        }

        // Only a request still there and approved is said to be: one removed or rejected while it waited isn't.
        if (notify && _db.Get(request.Id) is { Status: RequestStatus.Approved } approved)
        {
            _notifier.Notify(NotificationEvent.Approved, approved);
        }

        return request;
    }

    /// <summary>
    /// Declines a request that is still open.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="decider">The administrator's name.</param>
    /// <param name="reason">The reason shown to the requester, if any.</param>
    /// <param name="only">Declines only a request that also passes this check.</param>
    /// <param name="logActivity">Whether to write its activity entry; a bulk action writes one for all instead.</param>
    /// <returns>The updated request, or null when there is no such request.</returns>
    public MediaRequest? Decline(Guid id, string decider, string? reason, Func<MediaRequest, bool>? only = null, bool logActivity = true)
    {
        var request = Change(id, r => r.IsOpen && (only is null || only(r)), r =>
        {
            r.Status = RequestStatus.Declined;
            r.DecidedBy = decider;
            r.DecidedUtc = DateTime.UtcNow;
            r.Note = Truncate(reason, 500);
            r.Progress = null;
            r.ProgressText = null;
            r.RetryAfterUtc = null;
        });
        if (request is not null)
        {
            if (logActivity)
            {
                _activity.Log(
                    ServerWording.Get(TranslationKeys.RequestsActivityRejectedName),
                    ActivityType + ".Declined",
                    ActivityText.Sentence(ServerWording.Format(TranslationKeys.RequestsActivityRejected, decider, Describe(request), request.UserName)),
                    userId: request.UserId);
            }

            _notifier.Notify(NotificationEvent.Declined, request);
        }

        return request;
    }

    /// <summary>
    /// Marks an open request available by hand, for a title the library checks missed: with all its seasons, pointing at
    /// the requester's copy when there is one. The library checks then leave it so.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="decider">The administrator's name.</param>
    /// <returns>The updated request, or null when there is no such open request.</returns>
    public MediaRequest? MarkAvailable(Guid id, string decider)
    {
        var current = _db.Get(id);
        var requester = current is null || current.UserId == Guid.Empty ? null : _userManager.GetUserById(current.UserId);
        var item = current is null ? null : _library.Find(current.Type, current.ProviderIds, current.Title, current.Year, user: requester);
        var request = Change(id, r => r.IsOpen, r =>
        {
            r.Status = RequestStatus.Available;
            r.MarkedAvailable = true;
            r.LibraryItemId = item?.Id ?? r.LibraryItemId;
            r.AvailableSeasons = r.Type == RequestType.Series ? r.Seasons ?? r.ExpectedEpisodes?.Keys.Where(k => k > 0).Order().ToList() : null;
            r.Note = null;
            r.Progress = null;
            r.ProgressText = null;
            r.RetryAfterUtc = null;
            r.RemovedFromArr = false;
            if (r.DecidedUtc is null)
            {
                r.DecidedBy = decider;
                r.DecidedUtc = DateTime.UtcNow;
            }
        });
        if (request is not null)
        {
            Interlocked.Increment(ref _version);
            _activity.Log(
                ServerWording.Get(TranslationKeys.RequestsActivityMarkedAvailableName),
                ActivityType + ".Available",
                ActivityText.Sentence(ServerWording.Format(TranslationKeys.RequestsActivityMarkedAvailable, decider, Describe(request), request.UserName)),
                userId: request.UserId);
            _notifier.Notify(NotificationEvent.Available, request);
        }

        return request;
    }

    /// <summary>
    /// Removes a request. Requesters may remove their own while it is pending, or once Radarr or Sonarr has dropped
    /// it; administrators may remove any. A requester's own that others joined passes to the first who joined instead.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="access">Who is removing it.</param>
    /// <param name="logActivity">Whether to write its activity entry; a bulk action writes one for all instead.</param>
    /// <returns>True when it was removed.</returns>
    public bool Remove(Guid id, UserAccess access, bool logActivity = true)
    {
        ArgumentNullException.ThrowIfNull(access);
        string? handedTo = null;
        string? requester = null;
        Guid requesterId = Guid.Empty;
        var removed = _db.Atomic(() =>
        {
            var request = _db.Get(id);
            if (request is null || !MayRemove(request, access))
            {
                return null;
            }

            requester = request.UserName;
            requesterId = request.UserId;

            // Its requester stepping away from a request others joined hands it to the first of them who may still
            // request it, within their own limits, who keeps their place, rather than taking it from everyone. Joining
            // costs nothing, so the limits are checked here: otherwise two people could pass requests back and forth
            // past them.
            UserAccess? heir = null;
            if (request.UserId == access.UserId && request.Joined is { Count: > 0 } joined
                && joined.FirstOrDefault(j => (heir = GetAccess(j.UserId)) is { } a && MayInherit(request, a)) is { } next)
            {
                // On a profile the new requester may pick, their first when the one it has isn't.
                if (request.ArrId is null && heir is not null)
                {
                    var options = request.Type == RequestType.Movie ? heir.MovieProfiles : heir.ShowProfiles;
                    if (options is { Count: > 0 } && !options.Any(o => o.Id == request.ProfileId)
                        && ArrServers.Resolve(RequestConfig.Instance!.ReadConfiguration(c => c), request.Type, options[0].Id) is { } target)
                    {
                        request.ServerId = target.Server.Id;
                        request.ProfileId = target.Profile.Id;
                    }
                }

                // Approved as its requester made it: it isn't the new requester's name that approved it.
                if (string.Equals(request.DecidedBy, request.UserName, StringComparison.Ordinal) && request.Status != RequestStatus.Declined)
                {
                    request.DecidedBy = ServerWording.Get(TranslationKeys.RequestsValueApprovedAutomatically);
                }

                request.UserId = next.UserId;
                request.UserName = next.UserName;
                request.RequesterNote = null;
                var rest = joined.Where(j => j.UserId != next.UserId).ToList();
                request.Joined = rest.Count > 0 ? rest : null;
                request.UpdatedUtc = DateTime.UtcNow;
                _db.Update(request);
                handedTo = next.UserName;
                return request;
            }

            _db.Delete(id);
            return request;
        });
        if (removed is not null)
        {
            Interlocked.Increment(ref _version);

            // A request handed to someone who joined it lives on, so only a removed one's messages go.
            if (handedTo is null)
            {
                _notifier.Forget(removed.Id);
            }

            if (logActivity)
            {
                _activity.Log(
                    ServerWording.Get(TranslationKeys.RequestsActivityRemovedName),
                    ActivityType + ".Removed",
                    ActivityText.Sentence(handedTo is not null
                        ? ServerWording.Format(TranslationKeys.RequestsActivityRemovedHandedTo, access.UserName, Describe(removed), handedTo)
                        : requesterId == access.UserId
                            ? ServerWording.Format(TranslationKeys.RequestsActivityRemoved, access.UserName, Describe(removed))
                            : ServerWording.Format(TranslationKeys.RequestsActivityRemovedOther, access.UserName, Describe(removed), requester)),
                    userId: removed.UserId);
            }
        }

        return removed is not null;
    }

    /// <summary>
    /// Approves, declines or removes several requests at once.
    /// </summary>
    /// <param name="action">approve, decline or remove.</param>
    /// <param name="body">The request ids, and a reason for a decline.</param>
    /// <param name="admin">The administrator.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How it went.</returns>
    public async Task<BulkResult> BulkAsync(string action, BulkAction body, UserAccess admin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(admin);
        var result = new BulkResult();

        // One activity entry for the whole action, naming whose requests it changed.
        var requesters = new List<string>();
        foreach (var id in (body.Ids ?? new List<Guid>()).Distinct().Take(500))
        {
            switch (action)
            {
                case "approve":
                    // Each sent in the background, none waited for.
                    var approved = await ApproveAsync(id, admin.UserName, cancellationToken, waitFor: TimeSpan.Zero, logActivity: false).ConfigureAwait(false);
                    if (approved is null)
                    {
                        result.Skipped++;
                    }
                    else if (approved.Status == RequestStatus.Failed)
                    {
                        result.Failed++;
                        requesters.Add(approved.UserName);
                    }
                    else
                    {
                        result.Done++;
                        requesters.Add(approved.UserName);
                    }

                    break;
                case "decline":
                    if (Decline(id, admin.UserName, body.Reason, logActivity: false) is not { } declined)
                    {
                        result.Skipped++;
                    }
                    else
                    {
                        result.Done++;
                        requesters.Add(declined.UserName);
                    }

                    break;
                case "remove":
                    var held = _db.Get(id)?.UserName;
                    if (Remove(id, admin, logActivity: false))
                    {
                        result.Done++;
                        requesters.Add(held ?? string.Empty);
                    }
                    else
                    {
                        result.Skipped++;
                    }

                    break;
                default:
                    throw new ArgumentException("Unknown action.", nameof(action));
            }
        }

        if (requesters.Count > 0)
        {
            _activity.Log(
                RequestActivity.RunName(action, requesters.Count),
                ActivityType + "." + (action switch { "approve" => "Approved", "decline" => "Declined", _ => "Removed" }),
                RequestActivity.RunOverview(action, admin.UserName, requesters, result.Skipped));
        }

        return result;
    }

    // ------------------------------------------------------------------ fulfilment

    /// <summary>
    /// Updates every open request for this library movie or show: a movie, or a show with every requested
    /// season present, becomes available; a show with some of them becomes partially available.
    /// </summary>
    /// <param name="item">A movie or series in the library.</param>
    /// <param name="open">The open requests, when the caller already has them.</param>
    /// <returns>The requests that changed.</returns>
    public List<MediaRequest> Fulfill(BaseItem item, IReadOnlyList<MediaRequest>? open = null)
        => FulfillFrom(item, open ?? OpenRequests());

    /// <summary>Gets whether a user may see a library item, for pointing at it from someone else's request.</summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="access">The viewer.</param>
    /// <returns>Whether they may see it.</returns>
    public bool CanSee(Guid itemId, UserAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        // An item since removed hides nothing: the request shows until it's matched again.
        return access.IsAdministrator || (access.User is not null && (_library.Get(itemId) is not { } item || LibraryMatcher.IsVisibleTo(item, access.User)));
    }

    /// <summary>
    /// Lists the requests still open, for checking many library changes against them at once. The list is kept until
    /// a request is next written, and shared, so it's read-only.
    /// </summary>
    /// <returns>The open requests.</returns>
    public IReadOnlyList<MediaRequest> OpenRequests() => _db.OpenRequests();

    /// <summary>Gets a number that changes whenever a request is made, so a list of open requests read before it is known to be out of date.</summary>
    public long Version => Interlocked.Read(ref _version);

    private List<MediaRequest> FulfillFrom(BaseItem item, IReadOnlyList<MediaRequest> open)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (LibraryMatcher.KindOf(item) is not { } type)
        {
            return new List<MediaRequest>();
        }

        // Ids decide when they match; a request for the same title and year counts when no id says otherwise.
        var ids = ProviderIds.Clean(item.ProviderIds);
        var matched = open
            .Where(r => ProviderIds.Matches(r, type, ids)
                || (r.Type == type && r.Year.HasValue && !ProviderIds.Overlap(r.ProviderIds, ids) && LibraryMatcher.IsTitleMatch(item, r.ProviderIds, r.Title, r.Year.Value)))
            .ToList();
        if (matched.Count == 0)
        {
            return matched;
        }

        var counts = type == RequestType.Series ? _library.CountEpisodes(item) : null;
        var changed = new List<MediaRequest>();
        foreach (var match in matched)
        {
            // The copy its requester can see, when the one that turned up is in a library hidden from them (an
            // administrators' 4K library, say) and another copy is there for them. With none they can see, it stays
            // open: they can't watch it, so it isn't theirs yet.
            if (VisibleCopy(match, type, item) is not { } copy)
            {
                continue;
            }

            var copyCounts = ReferenceEquals(copy, item) || type != RequestType.Series ? counts : _library.CountEpisodes(copy);

            // Worked out from the row as it is now, inside the lock, so a send or approval that landed since the
            // list was read is never undone.
            var updated = Change(match.Id, r => r.IsOpen && Differs(r, Availability(r, type, copyCounts)), r =>
            {
                var (status, have) = Availability(r, type, copyCounts);
                // A failed send's note is what the server answered, which says nothing once it's arriving.
                if (r.Status == RequestStatus.Failed)
                {
                    r.Note = null;
                }

                r.Status = status;
                r.LibraryItemId = copy.Id;
                r.AvailableSeasons = have;
                if (status == RequestStatus.Available)
                {
                    r.Note = null;
                    r.Progress = null;
                    r.ProgressText = null;
                    r.RetryAfterUtc = null;
                }
            });
            if (updated is null)
            {
                continue;
            }

            changed.Add(updated);
            if (updated.Status == RequestStatus.Available)
            {
                _summary.Add(RequestActivity.Available, updated.UserName);
                if (!(_lostAvailable.TryRemove(updated.Id, out var lost) && DateTime.UtcNow - lost < ReturnQuietly))
                {
                    _notifier.Notify(NotificationEvent.Available, updated);
                }
            }
        }

        return changed;
    }

    // The item a request is fulfilled by: the one given when its requester may see it (or the requester is unknown),
    // else a copy of the same title they may see, else none.
    private BaseItem? VisibleCopy(MediaRequest request, RequestType type, BaseItem item)
    {
        var user = request.UserId == Guid.Empty ? null : _userManager.GetUserById(request.UserId);
        if (user is null || LibraryMatcher.IsVisibleTo(item, user))
        {
            return item;
        }

        return _library.Find(type, ProviderIds.Clean(item.ProviderIds), request.Title, request.Year, user: user) is { } visible && LibraryMatcher.KindOf(visible) == type
            ? visible
            : null;
    }

    /// <summary>
    /// Checks requests the library fulfilled against it as it is now: one whose movie or show is gone points at another
    /// copy of the title when there is one, and otherwise goes back to waiting for the library (and Radarr or Sonarr,
    /// once it was sent), so it's fulfilled again when the title comes back and the requester doesn't see it as there.
    /// A show that lost episodes holds only the seasons still whole.
    /// </summary>
    /// <param name="which">The fulfilled requests to check.</param>
    /// <returns>The requests that changed.</returns>
    public List<MediaRequest> Recheck(Func<MediaRequest, bool> which)
    {
        ArgumentNullException.ThrowIfNull(which);
        // One an administrator marked available stays so.
        return RecheckFrom(_db.List(statuses: new[] { RequestStatus.Available, RequestStatus.PartiallyAvailable }).Where(r => !r.MarkedAvailable).Where(which));
    }

    /// <summary>
    /// Checks the requests the library fulfilled with any of these movies or shows, as <see cref="Recheck(Func{MediaRequest, bool})"/>
    /// does, reading only those requests rather than every fulfilled one.
    /// </summary>
    /// <param name="libraryItemIds">The movies and shows that left the library or lost episodes.</param>
    /// <returns>The requests that changed.</returns>
    public List<MediaRequest> Recheck(IReadOnlyCollection<Guid> libraryItemIds)
    {
        ArgumentNullException.ThrowIfNull(libraryItemIds);
        return libraryItemIds.Count == 0 ? new List<MediaRequest>() : RecheckFrom(_db.FulfilledBy(libraryItemIds));
    }

    private List<MediaRequest> RecheckFrom(IEnumerable<MediaRequest> requests)
    {
        var changed = new List<MediaRequest>();
        foreach (var request in requests)
        {
            var current = request.LibraryItemId is { } id ? _library.Get(id) : null;
            // Another copy only counts when its requester can see it, as when it was fulfilled.
            var requester = request.UserId == Guid.Empty ? null : _userManager.GetUserById(request.UserId);
            var item = current is not null && LibraryMatcher.KindOf(current) == request.Type
                ? current
                : _library.Find(request.Type, request.ProviderIds, request.Title, request.Year, user: requester);

            // What it holds now: worked out as for a request still waiting, so it can go back as well as forward. One
            // the library fulfilled before anyone approved it goes back to waiting for approval, never to being sent.
            var reopened = request.DecidedUtc is null ? RequestStatus.Pending : RequestStatus.Approved;
            RequestStatus status;
            List<int>? have;
            if (item is null)
            {
                (status, have) = (reopened, null);
            }
            else
            {
                var probe = new MediaRequest { Type = request.Type, Seasons = request.Seasons, Specials = request.Specials, ExpectedEpisodes = request.ExpectedEpisodes, Status = reopened };
                (status, have) = Availability(probe, request.Type, request.Type == RequestType.Series ? _library.CountEpisodes(item) : null);
            }

            if (status == request.Status && item?.Id == request.LibraryItemId && (have ?? new List<int>()).SequenceEqual(request.AvailableSeasons ?? new List<int>()))
            {
                continue;
            }

            var updated = Change(
                request.Id,
                r => r.Status == request.Status && r.LibraryItemId == request.LibraryItemId,
                r =>
                {
                    r.Status = status;
                    r.LibraryItemId = item?.Id;
                    r.AvailableSeasons = have;
                });
            if (updated is null)
            {
                continue;
            }

            changed.Add(updated);
            Interlocked.Increment(ref _version);
            if (request.Status == RequestStatus.Available && status != RequestStatus.Available)
            {
                foreach (var old in _lostAvailable.Where(p => DateTime.UtcNow - p.Value >= ReturnQuietly).Select(p => p.Key).ToList())
                {
                    _lostAvailable.TryRemove(old, out _);
                }

                _lostAvailable[request.Id] = DateTime.UtcNow;
            }

            if (item is null || status != RequestStatus.Available)
            {
                _summary.Add(RequestActivity.Unavailable, updated.UserName, ServerWording.Get(item is null ? TranslationKeys.RequestsActivityReasonGone : TranslationKeys.RequestsActivityReasonPartlyGone));
            }
        }

        return changed;
    }

    /// <summary>
    /// Checks every open request against the library, for titles that arrived while the server was off or
    /// whose ids the scan did not know yet when they were added, and every fulfilled one whose movie or show is gone.
    /// </summary>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many requests changed.</returns>
    public Task<int> SweepAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var open = _db.List(statuses: OpenStatuses);
        var changed = 0;
        for (var i = 0; i < open.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = open[i];

            // The copy its requester can see first, so a hidden copy elsewhere isn't the one it points at.
            var requester = request.UserId == Guid.Empty ? null : _userManager.GetUserById(request.UserId);
            if (Enum.IsDefined(request.Type)
                && ((requester is null ? null : _library.Find(request.Type, request.ProviderIds, request.Title, request.Year, user: requester))
                    ?? _library.Find(request.Type, request.ProviderIds, request.Title, request.Year)) is { } item)
            {
                changed += Fulfill(item, open).Count;
            }

            progress?.Report(100.0 * (i + 1) / open.Count);
        }

        // Removed while the server was off.
        changed += Recheck(r => r.LibraryItemId is not { } id || _library.Get(id) is null).Count;
        return Task.FromResult(changed);
    }

    /// <summary>
    /// Reads back how Radarr and Sonarr are getting on with every approved request, and retries failed
    /// sends whose wait is over. One sync runs at a time: called while one is under way (the scheduled task and an
    /// administrator's refresh at once), it waits for that one to finish and returns without running again, since
    /// what that run found is as fresh as a second run would be.
    /// </summary>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once every request was checked.</returns>
    public async Task SyncProgressAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _syncing.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await _syncing.WaitAsync(cancellationToken).ConfigureAwait(false);
            _syncing.Release();
            progress?.Report(100);
            return;
        }

        try
        {
            await SyncProgressOnceAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _syncing.Release();
        }
    }

    private async Task SyncProgressOnceAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var retry = _db.List(statuses: new[] { RequestStatus.Failed })
            .Where(r => ArrClient.IsConfigured(r.Type) && !_sendBackoff.IsBackingOff(new BackoffState(r.SendFailures, r.RetryAfterUtc), now))
            .Select(r => r.Id)
            .ToList();
        var inFlight = _db.List(statuses: new[] { RequestStatus.Approved, RequestStatus.PartiallyAvailable })
            .Where(r => ArrClient.IsConfigured(r.Type))
            .ToList();
        var total = Math.Max(1, retry.Count + inFlight.Count);
        var done = 0;
        var arrived = new ConcurrentBag<string?>();

        foreach (var id in retry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claimed from the row as it is now: one declined, removed or approved since the list was read is skipped.
            var claimed = Change(id, r => r.Status == RequestStatus.Failed && !_sendBackoff.IsBackingOff(new BackoffState(r.SendFailures, r.RetryAfterUtc), DateTime.UtcNow), r =>
            {
                r.Status = RequestStatus.Approved;
                r.Note = null;
            });
            if (claimed is not null)
            {
                var sent = await SendAsync(claimed, CancellationToken.None).ConfigureAwait(false);
                if (sent.Status == RequestStatus.Approved)
                {
                    _summary.Add(RequestActivity.SentOnRetry, ArrClient.NameFor(sent));
                }
            }

            progress?.Report(100.0 * ++done / total);
        }

        // One request that can't be updated (the database busy, say) is logged and left for the next run, rather than
        // stopping every request after it.
        await ConcurrentTaskRunner.RunAsync(
            inFlight,
            async (request, ct) =>
            {
                try
                {
                    await SyncOneAsync(request, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Updating the status of request {Id} failed", request.Id);
                }
            },
            4,
            new Progress<double>(p => progress?.Report((100.0 * done / total) + (p * (total - done) / total))),
            cancellationToken).ConfigureAwait(false);

        foreach (var id in _filesSeen.Keys.Except(inFlight.Select(r => r.Id)))
        {
            _filesSeen.TryRemove(id, out _);
        }

        _refresher.Refresh(arrived.ToArray());
        _filesPrimed = true;
        progress?.Report(100);

        async Task SyncOneAsync(MediaRequest request, CancellationToken ct)
        {
            ArrStatus? status;
            try
            {
                status = await _arr.GetStatusAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One odd answer should not stop the others from being checked.
                _logger.LogWarning(ex, "Reading the status of request {Id} failed", request.Id);
                return;
            }

            if (status is null)
            {
                return;
            }

            // Approved but never reached Radarr or Sonarr: approved before it was set up, or the send was cut
            // short. Sent again once it has sat a while, so an approval still being sent is left alone.
            if (status.Missing && request.ArrId is null && request.IsInFlight && request.UpdatedUtc < DateTime.UtcNow.AddMinutes(-10) && !_queued.ContainsKey(request.Id))
            {
                await SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            // Dropped there and asked for again since, under a new id: the newer request holds that one, so this
            // stays dropped rather than both holding a place for one title.
            if (status.ArrId is { } found && found != request.ArrId && request.ArrId is not null && HeldByAnother(request, found, status.ServerId))
            {
                status = status with { ArrId = null, Missing = true };
            }

            // Only onto the request as it was read: one sent (or moved to another server) since was looked up as it
            // was before, so what was found says nothing about it now.
            var changed = Change(request.Id, r => r.IsInFlight && r.ArrId == request.ArrId && r.ServerId == request.ServerId, r => ApplyStatus(r, status), touch: false);

            // Files that weren't there last time: the library is refreshed for them rather than left to its next scan.
            if (changed is not null && status.Files > _filesSeen.GetValueOrDefault(request.Id))
            {
                _filesSeen[request.Id] = status.Files;
                if (_filesPrimed)
                {
                    arrived.Add(status.Folder);
                }
            }
        }
    }

    /// <summary>
    /// Removes declined and available requests older than the retention setting, and the Jellyfin messages still held
    /// about them.
    /// </summary>
    /// <returns>How many were removed.</returns>
    public int Cleanup()
    {
        // Requests left waiting too long are declined first, so the requester hears back.
        AutoDecline(DateTime.UtcNow);

        var days = RequestConfig.Instance?.ReadConfiguration(c => c.RetentionDays) ?? 0;
        if (days <= 0)
        {
            return 0;
        }

        // Requests inside the longest quota window still count toward it, so they stay until they age out of it.
        var quotaDays = RequestConfig.Instance?.ReadAll(PermissionRules.LongestQuotaDays) ?? 0;
        var removed = _db.DeleteClosedBefore(DateTime.UtcNow.AddDays(-days), quotaDays > 0 ? DateTime.UtcNow.AddDays(-quotaDays) : null);
        foreach (var id in removed)
        {
            _notifier.Forget(id);
        }

        if (removed.Count > 0)
        {
            _logger.LogInformation("Removed {Count} rejected and available requests older than {Days} days", removed.Count, days);
        }

        return removed.Count;
    }

    /// <summary>
    /// Rejects requests left waiting longer than the setting allows, with its reason. Run hourly with the reminders as
    /// well as by the daily cleanup, so a request is rejected within the hour its time comes.
    /// </summary>
    /// <param name="now">The time now; tests pass their own.</param>
    /// <returns>How many were rejected.</returns>
    public int AutoDecline(DateTime now)
    {
        var (declineAfter, declineReason) = RequestConfig.Instance?.ReadConfiguration(c => (c.AutoDeclineAfterDays, c.AutoDeclineReason)) ?? (0, string.Empty);
        if (declineAfter <= 0)
        {
            return 0;
        }

        var cutoff = now.AddDays(-declineAfter);
        var reason = string.IsNullOrWhiteSpace(declineReason) ? null : declineReason;
        var requesters = _db.List(statuses: new[] { RequestStatus.Pending }).Where(r => r.CreatedUtc < cutoff)
            .Select(stale => Decline(stale.Id, "Requests", reason, r => r.Status == RequestStatus.Pending, logActivity: false)?.UserName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
        if (requesters.Count > 0)
        {
            _activity.Log(
                RequestActivity.RunName("decline", requesters.Count),
                ActivityType + ".Declined",
                RequestActivity.ExpiredOverview(declineAfter, requesters));
        }

        return requesters.Count;
    }

    /// <summary>
    /// Reminds administrators about requests still waiting for approval: once when one has waited longer than the
    /// aging setting, and once a day in the last three days before one is rejected automatically. Each reminder is
    /// recorded on its request before it is sent, so a restart or a second run never sends it again.
    /// </summary>
    /// <param name="now">The time now; tests pass their own.</param>
    /// <returns>How many reminders were sent.</returns>
    public int Remind(DateTime now)
    {
        var (agingAfter, declineAfter) = RequestConfig.Instance?.ReadConfiguration(c => (c.AgingAfterDays, c.AutoDeclineAfterDays)) ?? (0, 0);
        if (agingAfter <= 0 && declineAfter <= 0)
        {
            return 0;
        }

        var sent = 0;
        foreach (var waiting in _db.List(statuses: new[] { RequestStatus.Pending }))
        {
            // Claimed from the row as it is now, so one approved since the list was read, or reminded by a run
            // alongside, isn't reminded about. Not a change to the request itself, so it isn't marked as updated.
            if (AgingDue(waiting, agingAfter, now)
                && Change(waiting.Id, r => AgingDue(r, agingAfter, now), r => r.AgingNotifiedUtc = now, touch: false) is { } aging)
            {
                _notifier.Notify(NotificationEvent.Aging, aging);
                sent++;
            }

            if (ExpiringDue(waiting, declineAfter, now) is { } left
                && Change(waiting.Id, r => ExpiringDue(r, declineAfter, now) == left, r => r.ExpiringNotifiedDays = left, touch: false) is { } expiring)
            {
                _notifier.Notify(NotificationEvent.Expiring, expiring);
                sent++;
            }
        }

        return sent;
    }

    /// <summary>Gets whether administrators are due a reminder that a request has waited long for approval.</summary>
    /// <param name="request">The request.</param>
    /// <param name="agingAfterDays">After how many days a waiting request is brought up; 0 never.</param>
    /// <param name="now">The time now.</param>
    /// <returns>True when it is still waiting, has waited that long, and hasn't been brought up before.</returns>
    internal static bool AgingDue(MediaRequest request, int agingAfterDays, DateTime now)
        => agingAfterDays > 0 && request.Status == RequestStatus.Pending && request.AgingNotifiedUtc is null
            && now - request.CreatedUtc >= TimeSpan.FromDays(agingAfterDays);

    /// <summary>
    /// Gets the days left that administrators are due to be told of before a waiting request is rejected
    /// automatically: 3, 2 or 1, each once. A day missed (the server was off) is skipped rather than sent late. Days
    /// left only go down, so one other than the last told of is a new day, or the setting was changed since.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="autoDeclineAfterDays">After how many days a waiting request is rejected; 0 never.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The days left to tell them of, or null when no reminder is due.</returns>
    internal static int? ExpiringDue(MediaRequest request, int autoDeclineAfterDays, DateTime now)
    {
        if (autoDeclineAfterDays <= 0 || request.Status != RequestStatus.Pending)
        {
            return null;
        }

        var left = RequestNotifier.DaysLeft(request.CreatedUtc, autoDeclineAfterDays, now);
        return left is >= 1 and <= 3 && request.ExpiringNotifiedDays != left ? left : null;
    }

    /// <summary>Counts requests by status for the dashboard.</summary>
    /// <returns>The counts.</returns>
    public RequestStats Stats() => Stats(null);

    /// <summary>
    /// Counts requests by status and type: everyone's, with each requester's, or one user's (those they made), with
    /// their quotas and open requests.
    /// </summary>
    /// <param name="user">The user, or null for everyone.</param>
    /// <returns>The counts.</returns>
    public RequestStats Stats(UserAccess? user)
    {
        var counts = _db.CountByUser(user?.UserId);
        if (user is not null)
        {
            var mine = Count(counts);
            mine.UserId = user.UserId;
            mine.UserName = user.UserName;
            mine.MovieQuota = user.MovieQuota;
            mine.ShowQuota = user.ShowQuota;
            mine.OpenRequests = user.OpenRequests;
            mine.MaxOpenRequests = user.MaxOpenRequests;
            return mine;
        }

        var all = Count(counts);
        all.Users = counts.GroupBy(c => c.UserId)
            .Select(g =>
            {
                var one = Count(g);
                one.UserId = g.Key;
                one.UserName = g.Select(c => c.UserName).FirstOrDefault(n => n.Length > 0);
                return one;
            })
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return all;
    }

    // Counts by status and type from rows of requester, type, status and count.
    private static RequestStats Count(IEnumerable<(Guid UserId, string UserName, RequestType Type, RequestStatus Status, int Count)> rows)
    {
        var list = rows.ToList();
        return new RequestStats
        {
            ByStatus = Enum.GetValues<RequestStatus>().ToDictionary(s => s.ToString(), s => list.Where(c => c.Status == s).Sum(c => c.Count)),
            Total = list.Sum(c => c.Count),
            Movies = list.Where(c => c.Type == RequestType.Movie).Sum(c => c.Count),
            Series = list.Where(c => c.Type == RequestType.Series).Sum(c => c.Count)
        };
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>What a requester is told when the request rules need details about a title that couldn't be read.</summary>
    internal const string CouldNotCheck = TranslationKeys.RequestsErrorCouldNotCheck;

    // Records what Radarr or Sonarr said about an in-flight request.
    internal static void ApplyStatus(MediaRequest r, ArrStatus status)
    {
        // From before servers were recorded: it was sent to the first listed, which it now records, id and all.
        if (r.ServerId is null && status.ServerId is not null)
        {
            r.ServerId = status.ServerId;
            r.ProfileId = status.ProfileId;
        }

        // Its server was removed, so it is now followed on the one that took over; the old id meant nothing there.
        else if (status.ServerId is not null && status.ServerId != r.ServerId)
        {
            r.ServerId = status.ServerId;
            r.ProfileId = status.ProfileId;
            r.ArrId = null;
        }

        // Removed from Radarr or Sonarr after it was sent, it keeps its id, so it isn't sent again (the administrator
        // may have removed it on purpose), and is marked as dropped so it stops holding a place. One never sent has
        // no id, and is sent once it has sat a while. Back there again, it holds its place again.
        r.ArrId = status.ArrId ?? r.ArrId;
        r.RemovedFromArr = status.Missing && r.ArrId is not null;
        r.Progress = status.Progress;
        r.ProgressText = status.Text;
        r.DownloadDoneUtc = status.DoneUtc;
        r.ExpectedEpisodes = status.ExpectedEpisodes ?? r.ExpectedEpisodes;
        r.SeasonProgress = status.Seasons ?? r.SeasonProgress;
    }

    // Whether someone may remove a request: administrators any; requesters their own while it waits for a decision,
    // or once Radarr or Sonarr has dropped it, since nothing will come of it then.
    internal static bool MayRemove(MediaRequest request, UserAccess access)
        => access.IsAdministrator || (request.UserId == access.UserId && (request.Status == RequestStatus.Pending || (request.IsOpen && request.RemovedFromArr)));

    // The open request a new one for these ids and seasons would repeat: only requests that hold a place count, so
    // one Radarr or Sonarr has dropped doesn't stop anyone asking again.
    // Specials are asked for apart from seasons (a whole show doesn't take them in), so a request for specials repeats
    // only another that asks for one of the same.
    internal static MediaRequest? Duplicate(IEnumerable<MediaRequest> open, RequestType type, IReadOnlyDictionary<string, string> ids, List<int>? seasons, List<SpecialEpisode>? specials = null)
        => open.FirstOrDefault(r => r.HoldsAPlace && ProviderIds.Matches(r, type, ids)
            && (specials is null
                ? r.Specials is null && SeasonsOverlap(OutstandingSeasons(r), seasons)
                : r.Specials is not null && r.Specials.Any(x => specials.Any(y => y.Number == x.Number))));

    // Why a requester may not request a title by the blocklist and the release rule, or null when they may. The
    // blocklist's own titles and words need no details; blocked genres and ratings, and the release rule, do, so when
    // those are set and the details couldn't be read the request is refused rather than let through unchecked.
    // Without Radarr or Sonarr, the name the providers know it by is checked against the blocked words as well.
    internal static string? RuleRefusal(RequestSettings config, UserAccess access, RequestType type, IReadOnlyDictionary<string, string> ids, string title, TitleFacts facts)
    {
        if (access.IsAdministrator)
        {
            return null;
        }

        if (Blocklist.Reason(config, type, ids, title, facts.Genres, facts.Rating, facts.Traits) is not null
            || (facts.Name is { Length: > 0 } name && !string.Equals(name, title, StringComparison.OrdinalIgnoreCase)
                && Blocklist.Reason(config, type, ids, name, facts.Genres, facts.Rating, facts.Traits) is not null))
        {
            return TranslationKeys.RequestsErrorTitleNotRequestable;
        }

        var unreleasedRule = access.Permissions?.Unreleased == false;
        if (!facts.Known && (config.BlockedGenres.Count > 0 || config.BlockedRatings.Count > 0 || unreleasedRule))
        {
            return CouldNotCheck;
        }

        // Keywords, languages and collections are TMDb's: without its answer they can't be checked.
        if (facts.Traits is null && Blocklist.NeedsTraits(config))
        {
            return CouldNotCheck;
        }

        // A title without a release date isn't out yet either.
        return unreleasedRule && facts.Released != true ? TranslationKeys.RequestsErrorNotOutYet : null;
    }

    internal static readonly RequestStatus[] OpenStatuses = { RequestStatus.Pending, RequestStatus.Approved, RequestStatus.Failed, RequestStatus.PartiallyAvailable };

    /// <summary>Names a request in the server's language: its title and year, and for a show what of it is wanted.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Such as Dune (2021), or Severance (2022), Seasons 1-2.</returns>
    internal static string Describe(MediaRequest request)
    {
        var name = request.Year.HasValue ? request.Title + " (" + request.Year.Value.ToString(CultureInfo.InvariantCulture) + ")" : request.Title;
        return request.Type == RequestType.Series && request.Seasons is { Count: > 0 } ? ServerWording.Format(TranslationKeys.RequestsTitleAndWanted, name, DescribeWanted(request)) : name;
    }

    /// <summary>Says what of a show a request asks for: its seasons, or its specials by name.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Such as All seasons, Seasons 1-3, or Specials: Christmas Special.</returns>
    internal static string DescribeWanted(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Specials is not { Count: > 0 } specials)
        {
            return DescribeSeasons(request.Seasons);
        }

        var names = specials.Select(x => string.IsNullOrWhiteSpace(x.Name) ? ServerWording.Format(TranslationKeys.RequestsSpecialNumber, x.Number) : x.Name!.Trim()).ToList();
        var listed = string.Join(", ", names.Take(5));
        if (names.Count > 5)
        {
            listed = ServerWording.Format(TranslationKeys.RequestsListAndMore, listed, names.Count - 5);
        }

        return ServerWording.Format(names.Count == 1 ? TranslationKeys.RequestsWantedSpecial : TranslationKeys.RequestsWantedSpecials, listed);
    }

    internal static string DescribeSeasons(List<int>? seasons)
    {
        if (seasons is not { Count: > 0 })
        {
            return ServerWording.Get(TranslationKeys.RequestsRequestWholeShow);
        }

        // Runs of consecutive seasons collapse: 1, 2, 3, 5 reads "Seasons 1-3, 5".
        var parts = new List<string>();
        var sorted = seasons.Order().ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var start = sorted[i];
            while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1)
            {
                i++;
            }

            parts.Add(start == sorted[i] ? start.ToString(CultureInfo.InvariantCulture) : start.ToString(CultureInfo.InvariantCulture) + "-" + sorted[i].ToString(CultureInfo.InvariantCulture));
        }

        return sorted.Count == 1 ? ServerWording.Format(TranslationKeys.NameSeasonNumber, parts[0]) : ServerWording.Format(TranslationKeys.RequestsSeasonNumbers, string.Join(", ", parts));
    }

    // Posters come from the metadata providers over https; anything else is dropped rather than rendered.
    // TMDb hands out its full-size originals, several megabytes each, so they are swapped for a poster-sized copy.
    internal static string? SafePosterUrl(string? url) => SafeImageUrl(url, "w342");

    // Backdrops get a wider TMDb copy than posters, still far smaller than the original.
    internal static string? SafeBackdropUrl(string? url) => SafeImageUrl(url, "w1280");

    internal static string? SafePersonUrl(string? url) => SafeImageUrl(url, "w185");

    // Only the artwork hosts Radarr, Sonarr and Jellyfin's providers use, so a request can't make every
    // administrator's browser load an address of the requester's choosing. Stored in Uri's escaped form, with
    // no control characters or quotes left to break out of a CSS url().
    private static readonly string[] _imageHosts = { "image.tmdb.org", "artworks.thetvdb.com", "thetvdb.com", "www.thetvdb.com", "assets.fanart.tv", "m.media-amazon.com" };

    private static string? SafeImageUrl(string? url, string tmdbSize)
    {
        if (url is not null && LibraryImage().IsMatch(url))
        {
            return url;
        }

        if (url is null || url.Length > 500 || url.Any(c => char.IsControl(c) || c is '"' or '\\' or '\'' or '(' or ')' or ' ')
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !_imageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) || !uri.IsDefaultPort)
        {
            return null;
        }

        var clean = uri.AbsoluteUri;
        return uri.Host == "image.tmdb.org" ? clean.Replace("/t/p/original/", "/t/p/" + tmdbSize + "/", StringComparison.Ordinal) : clean;
    }

    /// <summary>
    /// Returns the address of a library item's poster, relative to the request page, or null when it has none.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The address.</returns>
    internal static string? LibraryPosterUrl(BaseItem item)
        => item.HasImage(ImageType.Primary)
            ? "../Items/" + item.Id.ToString("N", CultureInfo.InvariantCulture) + "/Images/Primary?fillHeight=480&quality=90"
            : null;

    /// <summary>
    /// Returns the address of a library item's backdrop, relative to the request page, or null when it has none.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The address.</returns>
    internal static string? LibraryBackdropUrl(BaseItem item)
        => item.HasImage(ImageType.Backdrop)
            ? "../Items/" + item.Id.ToString("N", CultureInfo.InvariantCulture) + "/Images/Backdrop?fillWidth=1280&quality=80"
            : null;

    /// <summary>
    /// Returns the address of a library item's title logo, relative to the request page, or null when it has none.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <returns>The address.</returns>
    internal static string? LibraryLogoUrl(BaseItem item)
        => item.HasImage(ImageType.Logo)
            ? "../Items/" + item.Id.ToString("N", CultureInfo.InvariantCulture) + "/Images/Logo?maxWidth=600&quality=90"
            : null;

    // A library image this plugin built: the server's own image endpoint for an item, and nothing else.
    [GeneratedRegex(@"^\.\./Items/[0-9a-f]{32}/Images/(Primary\?fillHeight=480&quality=90|Backdrop\?fillWidth=1280&quality=80|Logo\?maxWidth=600&quality=90)$")]
    private static partial Regex LibraryImage();

    internal static List<int>? NormalizeSeasons(List<int>? seasons)
    {
        if (seasons is not { Count: > 0 })
        {
            return null;
        }

        // Season 0 holds specials, which Sonarr's season choices and the library's counts both leave out.
        return seasons.Any(s => s < 1 || s > MaxSeason) ? null : seasons.Distinct().Order().ToList();
    }

    // Null means the whole show, which overlaps any request for the same show.
    internal static bool SeasonsOverlap(List<int>? a, List<int>? b)
        => a is null || b is null || a.Intersect(b).Any();

    // Where a request stands against the library. A show is available once every season it asked for is
    // complete; a whole show, once every season Sonarr knows of is, or, without Sonarr, once the library has a
    // season and nothing it holds is missing episodes. Only an approved request becomes partially available:
    // one still waiting for a decision, or for a retry, keeps that state so it can still be approved or sent.
    internal static (RequestStatus Status, List<int>? Have) Availability(MediaRequest request, RequestType type, SeasonCounts? counts)
    {
        if (type == RequestType.Movie)
        {
            return (RequestStatus.Available, null);
        }

        counts ??= new SeasonCounts();

        // Specials are there once the library has each one asked for; until then the request stays as it stands.
        if (request.Specials is { Count: > 0 } specials)
        {
            return specials.All(counts.HasSpecial) ? (RequestStatus.Available, new List<int> { 0 }) : (request.Status, null);
        }

        var complete = LibraryMatcher.CompleteSeasons(counts, request.ExpectedEpisodes);
        var wanted = request.Seasons
            ?? (request.ExpectedEpisodes is { Count: > 0 } expected ? expected.Keys.Where(s => s > 0).Order().ToList() : null);
        List<int> have;
        bool all;
        if (wanted is null)
        {
            have = complete;
            all = complete.Count > 0 && counts.Have.Keys.Where(s => s > 0).All(complete.Contains);
        }
        else
        {
            have = wanted.Intersect(complete).Order().ToList();
            all = wanted.Count > 0 && have.Count == wanted.Count;
        }

        if (all)
        {
            return (RequestStatus.Available, have);
        }

        var partial = have.Count > 0 && request.Status is RequestStatus.Approved or RequestStatus.PartiallyAvailable;
        return (partial ? RequestStatus.PartiallyAvailable : request.Status, have.Count > 0 ? have : null);
    }

    // What an open request still waits for: its seasons less those already in, or the whole show.
    internal static List<int>? OutstandingSeasons(MediaRequest request)
        => request.Seasons is null ? null : request.Seasons.Except(request.AvailableSeasons ?? new List<int>()).ToList();

    private static bool Differs(MediaRequest request, (RequestStatus Status, List<int>? Have) now)
        => request.Status != now.Status || !SameSeasons(request.AvailableSeasons, now.Have);

    private static Dictionary<int, int>? Expected(List<SeasonInfo>? seasons)
        => seasons?.Where(s => s.EpisodeCount is > 0).ToDictionary(s => s.Number, s => s.EpisodeCount!.Value);

    // Genres, age rating and whether it is out, from Radarr or Sonarr's record, else the metadata providers. Known
    // says whether either could be read at all: the providers' lookup gives an empty record when it fails.
    private async Task<TitleFacts> FactsAsync(RequestType type, Dictionary<string, string> ids, string title, int? year, TmdbDescription? described, CancellationToken cancellationToken)
    {
        if (described is not null)
        {
            return new TitleFacts(described.Genres, described.Certification, described.Released, true) { Traits = described.Traits };
        }

        try
        {
            var details = await _details.GetTitleAsync(new DetailsLookup { Type = type, Title = title, Year = year, ProviderIds = ids }, cancellationToken).ConfigureAwait(false);
            return new TitleFacts(details.Genres, details.OfficialRating, details.ReleaseDate is { } date ? date <= DateTime.UtcNow : null, details.Name is not null) { Name = details.Name };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Looking up {Title} for the request rules failed", title);
            return new TitleFacts(new List<string>(), null, null, false);
        }
    }

    /// <summary>
    /// Adds a title to the blocklist, or removes it.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="blocked">True to block it, false to allow it again.</param>
    /// <param name="admin">Who changed it.</param>
    public void SetBlocked(BlockedTitle title, bool blocked, string admin)
    {
        ArgumentNullException.ThrowIfNull(title);
        var ids = Blocklist.IdsOf(title);

        // Under the configuration's lock, so two blocks at once, or a block during a settings save, both keep.
        RequestConfig.Instance!.MutateConfiguration(config =>
        {
            var list = (config.BlockedTitles ?? new()).Where(t => !Blocklist.Matches(t, title.Type, ids)).ToList();
            if (blocked)
            {
                list.Add(new BlockedTitle
                {
                    Type = title.Type,
                    Title = title.Title,
                    Year = title.Year,
                    Tmdb = ids.GetValueOrDefault(ProviderIds.Tmdb),
                    Tvdb = ids.GetValueOrDefault(ProviderIds.Tvdb),
                    Imdb = ids.GetValueOrDefault(ProviderIds.Imdb)
                });
            }

            config.BlockedTitles = list;
            return true;
        });
        _activity.Log(
            ServerWording.Get(blocked ? TranslationKeys.RequestsActivityBlockedName : TranslationKeys.RequestsActivityUnblockedName),
            ActivityType + (blocked ? ".Blocked" : ".Unblocked"),
            ActivityText.Sentence(ServerWording.Format(blocked ? TranslationKeys.RequestsActivityBlocked : TranslationKeys.RequestsActivityUnblocked, admin, title.Title)));
    }

    // Whether another open request, not dropped, already holds this id on this server.
    private bool HeldByAnother(MediaRequest request, int arrId, string? serverId)
        => _db.List(statuses: new[] { RequestStatus.Approved, RequestStatus.PartiallyAvailable, RequestStatus.Failed })
            .Any(r => r.Id != request.Id && r.Type == request.Type && r.ArrId == arrId && !r.RemovedFromArr && r.ServerId == (serverId ?? request.ServerId));

    private async Task<bool?> HasAsync(RequestType type, string title, int? year, Dictionary<string, string> ids, List<int>? seasons, CancellationToken cancellationToken)
    {
        try
        {
            return await _arr.HasAsync(type, title, year, ids, seasons, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Checking Radarr and Sonarr for the requested title failed");
            return null;
        }
    }

    private async Task<TmdbDescription?> DescribeAsync(RequestType type, Dictionary<string, string> ids, CancellationToken cancellationToken)
    {
        try
        {
            return await _tmdb.DescribeAsync(type, ids, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Looking up the requested title failed");
            return null;
        }
    }

    /// <summary>
    /// Works out a quota from what was requested in its window, oldest first, each with what it cost.
    /// </summary>
    /// <param name="used">When each request was made and what it cost, oldest first.</param>
    /// <param name="limit">The quota; 0 for none.</param>
    /// <param name="days">The window in days.</param>
    /// <param name="need">What the next request would cost, to say when there is room for it.</param>
    /// <returns>The quota, or null for none.</returns>
    internal static QuotaInfo? QuotaFrom(List<(DateTime At, int Cost)> used, int limit, int days, int need = 1)
    {
        if (limit <= 0)
        {
            return null;
        }

        var quota = new QuotaInfo { Limit = limit, Days = days, Used = used.Sum(u => u.Cost) };
        if (quota.Used + need > limit && need <= limit)
        {
            // Room comes back as the oldest requests age out of the window, until there is enough for the next.
            var freed = 0;
            foreach (var (at, cost) in used)
            {
                freed += cost;
                if (quota.Used - freed + need <= limit)
                {
                    quota.NextFreeUtc = at.AddDays(days);
                    break;
                }
            }
        }

        return quota;
    }

    /// <summary>Says that a quota is used up, and when the next request frees up.</summary>
    /// <param name="type">Movies or shows.</param>
    /// <param name="quota">The quota.</param>
    /// <param name="language">The reader's language, or null for US English.</param>
    /// <returns>The message.</returns>
    internal static string QuotaMessage(RequestType type, QuotaInfo quota, string? language = null)
    {
        var message = type == RequestType.Movie
            ? quota.Limit == 1 ? StolonTranslations.Format(TranslationKeys.RequestsQuotaUsedMovieOne, language, quota.Days) : StolonTranslations.Format(TranslationKeys.RequestsQuotaUsedMovies, language, quota.Limit, quota.Days)
            : quota.Limit == 1 ? StolonTranslations.Format(TranslationKeys.RequestsQuotaUsedShowOne, language, quota.Days) : StolonTranslations.Format(TranslationKeys.RequestsQuotaUsedShows, language, quota.Limit, quota.Days);

        if (quota.NextFreeUtc is not { } next)
        {
            return message;
        }

        // Said as a count of days rather than a date, which would be in the server's time zone, not the reader's.
        var days = Math.Max(1, (int)Math.Ceiling((next - DateTime.UtcNow).TotalDays));
        var nextOne = type == RequestType.Movie || quota.Remaining == 0;
        return message + " " + (days == 1
            ? StolonTranslations.Get(nextOne ? TranslationKeys.RequestsQuotaNextOneDay : TranslationKeys.RequestsQuotaEnoughOneDay, language)
            : StolonTranslations.Format(nextOne ? TranslationKeys.RequestsQuotaNextDays : TranslationKeys.RequestsQuotaEnoughDays, language, days));
    }

    private static bool SameSeasons(List<int>? a, List<int>? b)
        => (a is null && b is null) || (a is not null && b is not null && a.SequenceEqual(b));

    // A refusal in the language of whoever asked.
    private static string Say(UserAccess access, string key) => StolonTranslations.Get(key, access.Language);

    private static string? Truncate(string? text, int max)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > max ? text[..max] : text;
    }

    // A show quota: the shows requested in its window, each counted once from its first request, however many requests
    // for its seasons. One for a show already counted needs nothing more. except leaves out one request, the one being
    // changed.
    private QuotaInfo? ShowQuota(Guid userId, int limit, int days, IReadOnlyDictionary<string, string>? ids, Guid? except = null)
    {
        if (limit <= 0)
        {
            return null;
        }

        var shows = _db.RequestsSince(userId, RequestType.Series, DateTime.UtcNow.AddDays(-Math.Max(1, days))).Where(r => r.Id != except).ToList();
        var counted = new List<MediaRequest>();
        foreach (var r in shows.OrderBy(r => r.CreatedUtc))
        {
            if (!counted.Any(c => ProviderIds.Matches(c, RequestType.Series, r.ProviderIds)))
            {
                counted.Add(r);
            }
        }

        var need = ids is not null && counted.Any(c => ProviderIds.Matches(c, RequestType.Series, ids)) ? 0 : 1;
        var quota = QuotaFrom(counted.Select(c => (c.CreatedUtc, 1)).ToList(), limit, Math.Max(1, days), Math.Max(1, need));
        if (quota is not null)
        {
            quota.Need = need;
        }

        return quota;
    }

    // Whether a joiner may take over a request its requester removed: they may request the type, and holding it keeps
    // them within their open requests, quota and season limit, as making it themselves would have.
    private bool MayInherit(MediaRequest request, UserAccess heir)
    {
        if (!heir.CanRequest || !(request.Type == RequestType.Movie ? heir.Movies : heir.Series))
        {
            return false;
        }

        if (heir.MaxOpenRequests > 0 && _db.CountHoldingAPlace(heir.UserId) >= heir.MaxOpenRequests)
        {
            return false;
        }

        var permissions = heir.Permissions;
        var quota = permissions is null ? null : request.Type == RequestType.Movie
            ? Quota(heir.UserId, RequestType.Movie, permissions.MovieQuotaLimit, permissions.MovieQuotaDays, Math.Max(1, request.QuotaCost ?? 1))
            : ShowQuota(heir.UserId, permissions.ShowQuotaLimit, permissions.ShowQuotaDays, request.ProviderIds);
        if (quota is not null && quota.Need > 0 && quota.Used + quota.Need > quota.Limit)
        {
            return false;
        }

        return request.Type != RequestType.Series
            || SeasonCapRefusal(heir, request.ProviderIds, request.Seasons ?? request.ExpectedEpisodes?.Keys.Where(k => k > 0).ToList(), request.Id) is null;
    }

    // Why a show's seasons would pass the most one requester may have of it, counting their other requests for it
    // still open or fulfilled; null when they don't.
    private string? SeasonCapRefusal(UserAccess access, IReadOnlyDictionary<string, string> ids, List<int>? wanted, Guid? except)
    {
        if (access.MaxSeasonsPerShow <= 0 || wanted is null)
        {
            return null;
        }

        var had = _db.List(access.UserId, OpenStatuses.Append(RequestStatus.Available).ToArray())
            .Where(r => r.Id != except && ProviderIds.Matches(r, RequestType.Series, ids))
            .SelectMany(r => r.Seasons ?? r.ExpectedEpisodes?.Keys.Where(k => k > 0).ToList() ?? new List<int>());
        var total = had.Concat(wanted).Where(n => n > 0).Distinct().Count();
        var max = access.MaxSeasonsPerShow;
        return total <= max ? null
            : max == 1 ? StolonTranslations.Get(TranslationKeys.RequestsErrorSeasonCapOne, access.Language) : StolonTranslations.Format(TranslationKeys.RequestsErrorSeasonCap, access.Language, max);
    }

    private QuotaInfo? Quota(Guid userId, RequestType type, int limit, int days, int need = 1, Guid? except = null)
        => limit <= 0
            ? null
            : QuotaFrom(_db.RequestsSince(userId, type, DateTime.UtcNow.AddDays(-Math.Max(1, days))).Where(r => r.Id != except).Select(r => (r.CreatedUtc, 1)).ToList(), limit, Math.Max(1, days), need);

    // Sends an approved request to Radarr or Sonarr, recording the outcome. A failure backs off before the
    // status sync retries it, and only the first failure in a row is sent as an update.
    private async Task<MediaRequest> SendAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        // One send per request at a time: an approval and the status sync never both add the same title.
        if (!_sending.TryAdd(request.Id, 0))
        {
            return request;
        }

        (int? ArrId, string? Error) sent;
        try
        {
            // Read again so a request declined or removed since it was picked up is never sent. A show partly in the
            // library but never sent (approved before Sonarr was set up) is sent for the rest.
            var current = _db.Get(request.Id);
            if (current is null || current.Status is not (RequestStatus.Approved or RequestStatus.PartiallyAvailable))
            {
                return current ?? request;
            }

            request = current;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            sent = await _arr.AddAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sending request {Id} to {Server} failed", request.Id, ArrClient.NameFor(request));
            sent = (null, ex is OperationCanceledException ? ServerWording.Format(TranslationKeys.RequestsErrorArrTimedOut, ArrClient.NameFor(request)) : ServerWording.Get(TranslationKeys.RequestsErrorSendFailed));
        }
        finally
        {
            _sending.TryRemove(request.Id, out _);
        }

        if (sent.Error is null)
        {
            // Where it went, recorded straight away: the server that took over from a removed one, say. The id is kept
            // whatever the request's state now: one declined while it was being sent is still in Radarr or Sonarr, and
            // the administrator needs to know where.
            var placed = ArrClient.PlacementFor(request);
            return Change(request.Id, _ => true, r =>
            {
                r.ArrId = sent.ArrId ?? r.ArrId;
                if (sent.ArrId.HasValue && placed.ServerId is not null)
                {
                    r.ServerId = placed.ServerId;
                    r.ProfileId = placed.ProfileId;
                }

                if (r.Status == RequestStatus.PartiallyAvailable)
                {
                    r.Note = null;
                    r.SendFailures = 0;
                    r.RetryAfterUtc = null;
                    return;
                }

                if (r.Status is not (RequestStatus.Approved or RequestStatus.Failed))
                {
                    return;
                }

                r.Status = RequestStatus.Approved;
                r.RemovedFromArr = false;
                r.Note = null;
                r.SendFailures = 0;
                r.RetryAfterUtc = null;
                r.ProgressText = sent.ArrId.HasValue ? "Sent for download" : null;
            }) ?? request;
        }

        // A show part of which has arrived stays partly available, its progress kept, with the reason noted: what it
        // has is still there, and the status sync sends it again.
        var failed = Change(request.Id, r => r.Status is RequestStatus.Approved or RequestStatus.Failed or RequestStatus.PartiallyAvailable, r =>
        {
            var state = _sendBackoff.RecordFailure(new BackoffState(r.SendFailures, r.RetryAfterUtc));
            r.Note = sent.Error;
            r.SendFailures = state.ConsecutiveFailures;
            r.RetryAfterUtc = state.BackoffUntilUtc;
            if (r.Status == RequestStatus.PartiallyAvailable)
            {
                return;
            }

            r.Status = RequestStatus.Failed;
            r.Progress = null;
            r.ProgressText = null;
        }) ?? request;
        if (failed.SendFailures == 1 && failed.Status == RequestStatus.Failed)
        {
            _summary.Add(RequestActivity.NotSent, ArrClient.NameFor(failed), sent.Error);
            _notifier.Notify(NotificationEvent.Failed, failed);
        }

        return failed;
    }

    private MediaRequest? Change(Guid id, Func<MediaRequest, bool> allowed, Action<MediaRequest> change, bool touch = true)
    {
        return _db.Atomic(() =>
        {
            var request = _db.Get(id);
            if (request is null || !allowed(request))
            {
                return null;
            }

            // A change that leaves every field as it was isn't written, so the hourly sync of a request with nothing
            // new doesn't rewrite it, or make the open requests be read again.
            var before = RequestDatabase.StoredValues(request);
            change(request);
            if (RequestDatabase.StoredValues(request).SequenceEqual(before))
            {
                return request;
            }

            if (touch)
            {
                request.UpdatedUtc = DateTime.UtcNow;
            }

            _db.Update(request);
            return request;
        });
    }
}
