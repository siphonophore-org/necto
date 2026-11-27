using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Turns what happens to a request or an issue into Necto events, and sends them through Notifications, which picks
/// the channels and the people. A request's channels get one event, and its requester and those who joined each get
/// theirs in their own words. An issue's one event goes to every reporter and the channels.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class RequestNotifier
{
    private static readonly JsonSerializerOptions CopyJson = new();

    // A whole request or issue in a post, as the API returns it, with names rather than numbers.
    private static readonly JsonSerializerOptions RecordJson = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly INotifier _notifier;
    private readonly ILogger<RequestNotifier> _logger;
    private readonly IUserManager? _userManager;
    private readonly NotificationHeldStore? _held;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotifier"/> class.
    /// </summary>
    /// <param name="notifier">Sends each event to Notifications.</param>
    /// <param name="logger">Logs an event that could not be sent.</param>
    /// <param name="serverConfig">The server's settings, for the display language events are written in.</param>
    /// <param name="userManager">Finds the administrator who decided, and whether a joiner may see requesters, or <c>null</c>.</param>
    /// <param name="held">Holds Jellyfin messages for users away, so a removed request's can be dropped, or <c>null</c>.</param>
    public RequestNotifier(INotifier notifier, ILogger<RequestNotifier> logger, IServerConfigurationManager? serverConfig = null, IUserManager? userManager = null, INotifyHeldStore? held = null)
    {
        if (serverConfig is not null)
        {
            ServerWording.Use(serverConfig);
        }

        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userManager = userManager;
        _held = held as NotificationHeldStore;
    }

    /// <summary>Gets the name a request shows for a requester with none.</summary>
    internal static string UnnamedRequester => ServerWording.Get(TranslationKeys.RequestsValueSomeone);

    /// <summary>
    /// Sends a request's events in the background.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="request">The request as it stands.</param>
    public void Notify(NotificationEvent what, MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var copy = Copy(request);
        foreach (var nectoEvent in EventsFor(what, copy, Context(DateTime.UtcNow)))
        {
            _ = SendAsync(nectoEvent);
        }
    }

    /// <summary>
    /// Sends an issue's event in the background.
    /// </summary>
    /// <param name="what">What happened, <see cref="NotificationEvent.IssueReported"/> or <see cref="NotificationEvent.IssueResolved"/>.</param>
    /// <param name="issue">The issue as it stands.</param>
    public void NotifyIssue(NotificationEvent what, Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _ = SendAsync(IssueEvent(what, issue, Context(DateTime.UtcNow)));
    }

    /// <summary>
    /// Drops the Jellyfin messages still held about a request that was removed.
    /// </summary>
    /// <param name="requestId">The request.</param>
    public void Forget(Guid requestId) => _held?.RemoveSubject(RequestSubject(requestId));

    /// <summary>Gets the subject and browser tag of a request's events.</summary>
    /// <param name="requestId">The request.</param>
    /// <returns>Such as requests-0123….</returns>
    internal static string RequestSubject(Guid requestId) => "requests-" + requestId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Gets the subject and browser tag of an issue's events.</summary>
    /// <param name="issueId">The issue.</param>
    /// <returns>Such as issues-0123….</returns>
    internal static string IssueSubject(Guid issueId) => "issues-" + issueId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds every event a request's change sends. The channels get one event, worded for administrators. The
    /// requester gets theirs unless they approved it themselves, and those who joined get it without the requester's
    /// note or the reason, and without the requester's name unless they may see requesters. A reminder goes to the
    /// channels and the groups that pick it, never to the requester.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="request">The request.</param>
    /// <param name="context">The settings and lookups the events are built with.</param>
    /// <returns>The events, the channels' first.</returns>
    internal static IReadOnlyList<NectoEvent> EventsFor(NotificationEvent what, MediaRequest request, NoticeContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var requester = request.UserId == Guid.Empty ? (Guid?)null : request.UserId;
        var except = new List<Guid>();
        if (requester is { } asked)
        {
            except.Add(asked);
        }

        if (what is NotificationEvent.Approved or NotificationEvent.Declined && context.UserIdByName(request.DecidedBy) is { } decider)
        {
            except.Add(decider);
        }

        var reminder = what is NotificationEvent.Aging or NotificationEvent.Expiring;
        var channels = RequestEvent(what, request, context) with
        {
            Audience = reminder ? NectoEventAudience.All : NectoEventAudience.Channels,
            ExceptUserIds = except.Distinct().ToList(),
            Anonymous = RequestEvent(what, WithoutRequester(request), context) with { UserId = null }
        };
        var events = new List<NectoEvent> { channels };
        if (reminder)
        {
            return events;
        }

        // A requester who decided their own request needs no telling. Anyone who joined it still does.
        var selfApproved = what == NotificationEvent.Approved && !string.IsNullOrEmpty(request.DecidedBy)
            && string.Equals(request.DecidedBy, request.UserName, StringComparison.Ordinal);
        if (requester is not null && !selfApproved)
        {
            events.Add(UserEvent(what, OwnCopy(what, request), context, requester, Array.Empty<Guid>()));
        }

        var joiners = (request.Joined ?? new List<RequestJoiner>()).Select(j => j.UserId).Where(id => id != Guid.Empty && id != request.UserId).Distinct().ToList();
        foreach (var sees in new[] { true, false })
        {
            var these = joiners.Where(id => context.SeesRequesters(id) == sees).ToList();
            if (these.Count > 0)
            {
                events.Add(UserEvent(what, JoinerCopy(request, sees), context, null, these));
            }
        }

        return events;
    }

    /// <summary>
    /// Builds the channels' event for a request, worded for administrators.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="request">The request.</param>
    /// <param name="context">The settings the event is built with.</param>
    /// <returns>The event, about the requester.</returns>
    internal static NectoEvent RequestEvent(NotificationEvent what, MediaRequest request, NoticeContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var values = Values(what, request, context.AutoDeclineAfterDays, context.Now);
        var (title, body) = Wording(what);
        var ids = request.ProviderIds ?? new Dictionary<string, string>();
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event"] = what.ToString(),
            ["type"] = request.Type == RequestType.Series ? "Series" : "Movie",
            ["requestId"] = request.Id.ToString("N", CultureInfo.InvariantCulture),
            ["status"] = request.Status.ToString()
        };
        foreach (var (key, id) in ids.Select(p => (p.Key, p.Value)).Where(p => !string.IsNullOrEmpty(p.Value)))
        {
            data[key.ToLowerInvariant() + "Id"] = id;
        }

        if (request.LibraryItemId is { } libraryItem)
        {
            data["libraryItemId"] = libraryItem.ToString("N", CultureInfo.InvariantCulture);
        }

        values["joinerIds"] = string.Join(',', (request.Joined ?? new()).Where(j => j.UserId != Guid.Empty).Select(j => j.UserId.ToString("N", CultureInfo.InvariantCulture)));
        var subject = RequestSubject(request.Id);
        return new NectoEvent(RequestEvents.KeyFor(what), Fill(title, values), Fill(body, values))
        {
            UserId = request.UserId == Guid.Empty ? null : request.UserId,
            Url = RequestLink(request),
            Level = what switch
            {
                NotificationEvent.Failed => NectoEventLevel.Error,
                NotificationEvent.Aging or NotificationEvent.Expiring => NectoEventLevel.Warning,
                _ => NectoEventLevel.Info
            },
            Data = data,
            Values = values,
            Fields = RequestFields(request),
            ImageUrl = PosterUrl(request.PosterUrl, context.ServerUrl),

            // One nudge on the most recent app is enough, and an expiring count is out of date by the next day's.
            OneDevice = what == NotificationEvent.Aging,
            Hold = what != NotificationEvent.Expiring,
            Tag = subject,
            Subject = subject
        };
    }

    /// <summary>
    /// Builds the event for an issue, which goes to every reporter and the channels. The administrators leave out
    /// the reporters of a new issue and whoever decided one.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="issue">The issue.</param>
    /// <param name="context">The settings and lookups the event is built with.</param>
    /// <returns>The event, about the first reporter.</returns>
    internal static NectoEvent IssueEvent(NotificationEvent what, Issue issue, NoticeContext context)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(context);
        var reporters = issue.Reporters.Select(r => r.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        var except = what == NotificationEvent.IssueReported
            ? reporters
            : context.UserIdByName(issue.DecidedBy) is { } decider ? new List<Guid> { decider } : new List<Guid>();
        var made = IssueEventFor(what, issue, context);
        return made with
        {
            OtherUserIds = reporters.Skip(1).ToList(),
            ExceptUserIds = except,
            Anonymous = IssueEventFor(what, WithoutReporters(issue), context) with { UserId = null }
        };
    }

    /// <summary>Gets an event's title and body templates, in the server's display language.</summary>
    /// <param name="what">The event.</param>
    /// <returns>The templates, with <c>{name}</c> tokens.</returns>
    internal static (string Title, string Body) Wording(NotificationEvent what)
    {
        var (title, message) = what switch
        {
            NotificationEvent.Created => (TranslationKeys.RequestsUpdateCreatedTitle, TranslationKeys.RequestsUpdateCreatedMessage),
            NotificationEvent.Approved => (TranslationKeys.RequestsUpdateApprovedTitle, TranslationKeys.RequestsUpdateApprovedMessage),
            NotificationEvent.Declined => (TranslationKeys.RequestsUpdateDeclinedTitle, TranslationKeys.RequestsUpdateDeclinedMessage),
            NotificationEvent.Available => (TranslationKeys.RequestsUpdateAvailableTitle, TranslationKeys.RequestsUpdateAvailableMessage),
            NotificationEvent.Aging => (TranslationKeys.RequestsUpdateAgingTitle, TranslationKeys.RequestsUpdateAgingMessage),
            NotificationEvent.Expiring => (TranslationKeys.RequestsUpdateExpiringTitle, TranslationKeys.RequestsUpdateExpiringMessage),
            NotificationEvent.IssueReported => (TranslationKeys.RequestsUpdateIssueReportedTitle, TranslationKeys.RequestsUpdateIssueReportedMessage),
            NotificationEvent.IssueResolved => (TranslationKeys.RequestsUpdateIssueResolvedTitle, TranslationKeys.RequestsUpdateIssueResolvedMessage),
            _ => (TranslationKeys.RequestsUpdateFailedTitle, TranslationKeys.RequestsUpdateFailedMessage)
        };
        return (ServerWording.Get(title), ServerWording.Get(message));
    }

    /// <summary>
    /// Fills a template's <c>{name}</c> tokens, leaving unknown ones out, and tidies the spaces an empty value leaves.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="values">The values by token name.</param>
    /// <returns>The text.</returns>
    internal static string Fill(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var filled = TokenPattern().Replace(template ?? string.Empty, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : string.Empty);
        return SpacePattern().Replace(filled, " ").Trim();
    }

    /// <summary>Gets each token's value for a request.</summary>
    /// <param name="what">The event, which decides what {days} counts.</param>
    /// <param name="request">The request.</param>
    /// <param name="autoDeclineAfterDays">After how many days a waiting request is rejected.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The values by token name.</returns>
    internal static Dictionary<string, string> Values(NotificationEvent what, MediaRequest request, int autoDeclineAfterDays, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var days = what == NotificationEvent.Expiring
            ? request.ExpiringNotifiedDays ?? Math.Max(0, DaysLeft(request.CreatedUtc, autoDeclineAfterDays, now))
            : DaysWaiting(request.CreatedUtc, request.AgingNotifiedUtc ?? now);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = RequestService.Describe(request),
            ["name"] = request.Title ?? string.Empty,
            ["year"] = request.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["type"] = ServerWording.Get(request.Type == RequestType.Series ? TranslationKeys.LabelShow : TranslationKeys.Movie),
            ["seasons"] = request.Type == RequestType.Series ? RequestService.DescribeSeasons(request.Seasons) : string.Empty,
            ["user"] = string.IsNullOrEmpty(request.UserName) ? UnnamedRequester : request.UserName,
            ["joined"] = (request.Joined?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            ["note"] = request.RequesterNote ?? string.Empty,
            ["reason"] = request.Note ?? string.Empty,
            ["decidedBy"] = request.DecidedBy ?? string.Empty,
            ["overview"] = request.Overview ?? string.Empty,
            ["days"] = days == 1 ? ServerWording.Get(TranslationKeys.DayCountOne) : ServerWording.Format(TranslationKeys.DayCount, days)
        };
    }

    /// <summary>Gets each token's value for an issue, in the server's display language.</summary>
    /// <param name="issue">The issue.</param>
    /// <returns>The values by token name.</returns>
    internal static Dictionary<string, string> IssueValues(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var language = ServerWording.Language;
        var state = ServerWording.Get(issue.State switch
        {
            IssueState.Confirmed => TranslationKeys.StateConfirmed,
            IssueState.Resolved => TranslationKeys.StateResolved,
            IssueState.Declined => TranslationKeys.StateDeclined,
            IssueState.Rescinded => TranslationKeys.StateRescinded,
            _ => TranslationKeys.StateReported
        });
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = IssueService.NotifyTitle(issue, language),
            ["name"] = issue.Title ?? string.Empty,
            ["year"] = issue.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["type"] = ServerWording.Get(issue.ItemType switch
            {
                "Movie" => TranslationKeys.Movie,
                "Episode" => TranslationKeys.Episode,
                "MusicVideo" => TranslationKeys.MusicVideo,
                "Audio" or "AudioBook" => TranslationKeys.Audio,
                _ => TranslationKeys.Video
            }),
            ["user"] = issue.FirstReporter?.UserName is { Length: > 0 } reporter ? reporter : UnnamedRequester,
            ["joined"] = Math.Max(0, issue.Reporters.Count - 1).ToString(CultureInfo.InvariantCulture),
            ["note"] = ReporterNote(issue),
            ["reason"] = issue.Notes.LastOrDefault(n => n.Admin)?.Text ?? string.Empty,
            ["decidedBy"] = issue.DecidedBy ?? string.Empty,
            ["issueType"] = issue.FirstReporter is { } first ? IssueService.TypeName(first.Type, language) : string.Empty,
            ["state"] = state.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Gets the page a request's notification opens: the title in the library once it is there, otherwise the title on
    /// the request page.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The address, relative to the server.</returns>
    internal static string RequestLink(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LibraryItemId is { } item)
        {
            return "web/#/details?id=" + item.ToString("N", CultureInfo.InvariantCulture);
        }

        // The request page's own address for a title, as its titleHash writes it.
        var ids = request.ProviderIds ?? new Dictionary<string, string>();
        var parts = new List<(string Key, string? Value)>
        {
            ("type", request.Type == RequestType.Series ? "Series" : "Movie"),
            ("name", request.Title),
            ("tmdb", ids.GetValueOrDefault(ProviderIds.Tmdb)),
            ("tvdb", ids.GetValueOrDefault(ProviderIds.Tvdb)),
            ("imdb", ids.GetValueOrDefault(ProviderIds.Imdb))
        };
        return "requests/#/details?" + string.Join('&', parts.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value!).Replace("%20", "+", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Gets a poster as an address a service outside the server can load. A library poster is stored relative to the
    /// request page, as ../Items/..., and is made absolute with the server's address, or left out when none is set.
    /// </summary>
    /// <param name="posterUrl">The poster as stored.</param>
    /// <param name="serverUrl">The server's public address, or empty.</param>
    /// <returns>The absolute http or https address, or <c>null</c>.</returns>
    internal static string? PosterUrl(string? posterUrl, string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(posterUrl))
        {
            return null;
        }

        if (posterUrl.StartsWith("../", StringComparison.Ordinal))
        {
            return ArrClient.TryNormalizeUrl(serverUrl, out var server) ? Absolute(server + "/" + posterUrl[3..]) : null;
        }

        return Absolute(posterUrl);
    }

    /// <summary>Gets an issue's poster, the item's or its series', from the server's address.</summary>
    /// <param name="issue">The issue.</param>
    /// <param name="serverUrl">The server's public address, or empty.</param>
    /// <returns>The address, or <c>null</c> when no server address is set.</returns>
    internal static string? IssuePosterUrl(Issue issue, string? serverUrl)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var item = issue.SeriesId ?? issue.ItemId;
        return ArrClient.TryNormalizeUrl(serverUrl, out var server)
            ? Absolute(server + "/Items/" + item.ToString("N", CultureInfo.InvariantCulture) + "/Images/Primary?fillHeight=480&quality=90")
            : null;
    }

    /// <summary>
    /// Gets a copy of a request that names no requester: Someone where it says who asked, no user id, no name where
    /// they decided it themselves, and those who joined counted but not named.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The copy.</returns>
    internal static MediaRequest WithoutRequester(MediaRequest request)
    {
        var copy = Copy(request);
        if (!string.IsNullOrEmpty(copy.DecidedBy) && string.Equals(copy.DecidedBy, copy.UserName, StringComparison.Ordinal))
        {
            copy.DecidedBy = copy.Status == RequestStatus.Declined ? null : ServerWording.Get(TranslationKeys.RequestsValueApprovedAutomatically);
        }

        copy.UserName = UnnamedRequester;
        copy.UserId = Guid.Empty;
        copy.Joined = copy.Joined?.Select(j => new RequestJoiner { JoinedUtc = j.JoinedUtc }).ToList();
        return copy;
    }

    /// <summary>Gets the whole days left before a waiting request is rejected, rounded up.</summary>
    /// <param name="createdUtc">When the request was made.</param>
    /// <param name="autoDeclineAfterDays">After how many days a waiting request is rejected.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The days left, 0 or less once the time has come.</returns>
    internal static int DaysLeft(DateTime createdUtc, int autoDeclineAfterDays, DateTime now)
        => (int)Math.Ceiling((createdUtc.AddDays(autoDeclineAfterDays) - now).TotalDays);

    /// <summary>Gets the whole days a request has waited since it was made.</summary>
    /// <param name="createdUtc">When the request was made.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The days, never below 0.</returns>
    internal static int DaysWaiting(DateTime createdUtc, DateTime now)
        => Math.Max(0, (int)Math.Floor((now - createdUtc).TotalDays));

    // The issue's event before its people are set: its words, data, fields, poster, and link.
    private static NectoEvent IssueEventFor(NotificationEvent what, Issue issue, NoticeContext context)
    {
        var values = IssueValues(issue);
        var (title, body) = Wording(what);
        var ids = issue.ItemType == "Episode" && issue.SeriesProviderIds is { Count: > 0 } series ? series : issue.ProviderIds;
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["event"] = what.ToString(),
            ["type"] = issue.ItemType ?? string.Empty,
            ["issueId"] = issue.Id.ToString("N", CultureInfo.InvariantCulture),
            ["state"] = issue.State.ToString(),
            ["itemId"] = issue.ItemId.ToString("N", CultureInfo.InvariantCulture),
            ["issueType"] = values["issueType"]
        };
        foreach (var (key, id) in (ids ?? new Dictionary<string, string>()).Select(p => (p.Key, p.Value)).Where(p => !string.IsNullOrEmpty(p.Value)))
        {
            data[key.ToLowerInvariant() + "Id"] = id;
        }

        values["reporterIds"] = string.Join(',', issue.Reporters.Where(r => r.UserId != Guid.Empty).Select(r => r.UserId.ToString("N", CultureInfo.InvariantCulture)));
        var subject = IssueSubject(issue.Id);
        var first = issue.FirstReporter?.UserId;
        return new NectoEvent(IssueEvents.KeyFor(what), Fill(title, values), Fill(body, values))
        {
            UserId = first is { } reporterId && reporterId != Guid.Empty ? reporterId : null,
            Url = "issues/#/item?id=" + issue.ItemId.ToString("N", CultureInfo.InvariantCulture),
            Level = what == NotificationEvent.IssueReported ? NectoEventLevel.Warning : NectoEventLevel.Info,
            Data = data,
            Values = values,
            Fields = new[]
            {
                new NectoEventField(PostFields.User, ServerWording.Get(TranslationKeys.IssuesLabelReporter), issue.FirstReporter?.UserName ?? string.Empty),
                new NectoEventField(PostFields.Joined, ServerWording.Get(TranslationKeys.RequestsLabelJoined), issue.Reporters.Count > 1 ? (issue.Reporters.Count - 1).ToString(CultureInfo.InvariantCulture) : string.Empty),
                new NectoEventField("issueType", ServerWording.Get(TranslationKeys.IssuesLabelType), values["issueType"]),
                new NectoEventField(PostFields.Note, ServerWording.Get(TranslationKeys.LabelNotes), ReporterNote(issue), Inline: false),
                new NectoEventField(PostFields.Issue, PostFields.Issue, JsonSerializer.Serialize(issue, RecordJson)) { Json = true }
            },
            ImageUrl = IssuePosterUrl(issue, context.ServerUrl),
            Hold = true,
            Tag = subject,
            Subject = subject
        };
    }

    // The fields a request's posts carry, each as its channel allows.
    private static NectoEventField[] RequestFields(MediaRequest request) => new[]
    {
        new NectoEventField(PostFields.Overview, ServerWording.Get(TranslationKeys.Overview), request.Overview ?? string.Empty, Inline: false) { InText = true },
        new NectoEventField(PostFields.User, ServerWording.Get(TranslationKeys.RequestsLabelRequester), request.UserName ?? string.Empty),
        new NectoEventField(PostFields.Joined, ServerWording.Get(TranslationKeys.RequestsLabelJoined), request.Joined is { Count: > 0 } joined ? joined.Count.ToString(CultureInfo.InvariantCulture) : string.Empty),
        new NectoEventField(PostFields.Seasons, ServerWording.Get(TranslationKeys.HeaderSeasons), request.Type == RequestType.Series ? RequestService.DescribeWanted(request) : string.Empty),
        new NectoEventField(PostFields.Note, ServerWording.Get(TranslationKeys.LabelNotes), request.RequesterNote ?? string.Empty, Inline: false),
        new NectoEventField(PostFields.Request, PostFields.Request, JsonSerializer.Serialize(request, RecordJson)) { Json = true }
    };

    // An event only users get, in the words its copy of the request gives.
    private static NectoEvent UserEvent(NotificationEvent what, MediaRequest worded, NoticeContext context, Guid? userId, IReadOnlyList<Guid> others)
        => RequestEvent(what, worded, context) with
        {
            Audience = NectoEventAudience.Users,
            UserId = userId,
            OtherUserIds = others,
            Fields = Array.Empty<NectoEventField>()
        };

    // The requester's copy: a refusal reads that it is tried again, never Radarr's or Sonarr's own error.
    private static MediaRequest OwnCopy(NotificationEvent what, MediaRequest request)
    {
        if (what != NotificationEvent.Failed)
        {
            return request;
        }

        var copy = Copy(request);
        copy.Note = ServerWording.Get(TranslationKeys.RequestsNoteTriedAgain);
        return copy;
    }

    // A joiner's copy: without the requester's note or the reason, which were written to the requester.
    private static MediaRequest JoinerCopy(MediaRequest request, bool seesRequester)
    {
        var copy = seesRequester ? Copy(request) : WithoutRequester(request);
        copy.Note = null;
        copy.RequesterNote = null;
        return copy;
    }

    // A copy of an issue that names no reporter: each counted, none named, and no reporter's note.
    private static Issue WithoutReporters(Issue issue)
    {
        var copy = JsonSerializer.Deserialize<Issue>(JsonSerializer.Serialize(issue, CopyJson), CopyJson) ?? issue;
        copy.Reporters = copy.Reporters.Select(r => new IssueReporter { Type = r.Type, ReportedUtc = r.ReportedUtc }).ToList();
        copy.Notes = copy.Notes.Where(n => n.Admin).ToList();
        return copy;
    }

    private static string ReporterNote(Issue issue)
    {
        var first = issue.FirstReporter?.UserId;
        return issue.Notes.FirstOrDefault(n => !n.Admin && n.UserId == first)?.Text ?? string.Empty;
    }

    private static string? Absolute(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri.AbsoluteUri : null;

    // A copy taken now, so a change to the request before the send lands cannot change what is sent.
    private static MediaRequest Copy(MediaRequest request)
        => JsonSerializer.Deserialize<MediaRequest>(JsonSerializer.Serialize(request, CopyJson), CopyJson) ?? request;

    // The settings and lookups for events built now, from the loaded plugin and the server's users.
    private NoticeContext Context(DateTime now)
    {
        var config = Plugin.Instance?.ReadConfiguration(ConfigurationCopy.Of) ?? new PluginConfiguration();
        var users = _userManager;
        return new NoticeContext(
            config.Requests?.AutoDeclineAfterDays ?? 0,
            now,
            config.Notifications?.ServerUrl ?? string.Empty,
            name => string.IsNullOrEmpty(name) ? null : users?.GetUserByName(name)?.Id,
            id =>
            {
                var user = users?.GetUserById(id);
                if (user is null)
                {
                    return false;
                }

                var admin = user.HasPermission(PermissionKind.IsAdministrator);
                return admin || PermissionService.Resolve(config, id, admin).Requests.MaySeeRequesters;
            });
    }

    private async Task SendAsync(NectoEvent nectoEvent)
    {
        try
        {
            await _notifier.SendAsync(nectoEvent, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sent in the background, where nothing else would see it fail.
            _logger.LogWarning(ex, "Could not send the {Key} event", nectoEvent.Key);
        }
    }

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex SpacePattern();
}
