using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Turns what happens to a request or an issue into a Necto event, and sends it through Notifications, which picks
/// the channels and the people. The event is about the requester or the first reporter.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class RequestNotifier
{
    private static readonly JsonSerializerOptions CopyJson = new();

    private readonly INotifier _notifier;
    private readonly ILogger<RequestNotifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestNotifier"/> class.
    /// </summary>
    /// <param name="notifier">Sends each event to Notifications.</param>
    /// <param name="logger">Logs an event that could not be sent.</param>
    /// <param name="serverConfig">The server's settings, for the display language events are written in.</param>
    public RequestNotifier(INotifier notifier, ILogger<RequestNotifier> logger, IServerConfigurationManager? serverConfig = null)
    {
        if (serverConfig is not null)
        {
            ServerWording.Use(serverConfig);
        }

        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the name a request shows for a requester with none.</summary>
    internal static string UnnamedRequester => ServerWording.Get(TranslationKeys.RequestsValueSomeone);

    /// <summary>
    /// Sends a request's event in the background.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="request">The request as it stands.</param>
    public void Notify(NotificationEvent what, MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var nectoEvent = RequestEvent(what, Copy(request), Plugin.Instance?.ReadConfiguration(c => c.Requests?.AutoDeclineAfterDays) ?? 0, DateTime.UtcNow);
        _ = SendAsync(nectoEvent);
    }

    /// <summary>
    /// Sends an issue's event in the background.
    /// </summary>
    /// <param name="what">What happened, <see cref="NotificationEvent.IssueReported"/> or <see cref="NotificationEvent.IssueResolved"/>.</param>
    /// <param name="issue">The issue as it stands.</param>
    public void NotifyIssue(NotificationEvent what, Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _ = SendAsync(IssueEvent(what, issue));
    }

    /// <summary>
    /// Builds the event for a request.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="request">The request.</param>
    /// <param name="autoDeclineAfterDays">After how many days a waiting request is rejected, for an expiring one.</param>
    /// <param name="now">The time now.</param>
    /// <returns>The event, about the requester.</returns>
    internal static NectoEvent RequestEvent(NotificationEvent what, MediaRequest request, int autoDeclineAfterDays, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = Values(what, request, autoDeclineAfterDays, now);
        var (title, body) = Wording(what);
        var data = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["event"] = what.ToString(),
            ["requestId"] = request.Id.ToString("N", CultureInfo.InvariantCulture),
            ["status"] = request.Status.ToString(),
            ["joinerIds"] = string.Join(',', (request.Joined ?? new()).Select(j => j.UserId.ToString("N", CultureInfo.InvariantCulture)))
        };
        foreach (var (key, id) in (request.ProviderIds ?? new Dictionary<string, string>()).Select(p => (p.Key, p.Value)))
        {
            data[key.ToLowerInvariant() + "Id"] = id;
        }

        return new NectoEvent(RequestEvents.KeyFor(what), Fill(title, values), Fill(body, values))
        {
            UserId = request.UserId == Guid.Empty ? null : request.UserId,
            Url = request.LibraryItemId is { } item ? "web/#/details?id=" + item.ToString("N", CultureInfo.InvariantCulture) : "requests/",
            Level = what switch
            {
                NotificationEvent.Failed => NectoEventLevel.Error,
                NotificationEvent.Aging or NotificationEvent.Expiring => NectoEventLevel.Warning,
                _ => NectoEventLevel.Info
            },
            Data = data
        };
    }

    /// <summary>
    /// Builds the event for an issue.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <param name="issue">The issue.</param>
    /// <returns>The event, about the first reporter.</returns>
    internal static NectoEvent IssueEvent(NotificationEvent what, Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var values = IssueValues(issue);
        var (title, body) = Wording(what);
        var data = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            ["event"] = what.ToString(),
            ["issueId"] = issue.Id.ToString("N", CultureInfo.InvariantCulture),
            ["itemId"] = issue.ItemId.ToString("N", CultureInfo.InvariantCulture),
            ["reporterIds"] = string.Join(',', issue.Reporters.Select(r => r.UserId.ToString("N", CultureInfo.InvariantCulture)))
        };
        return new NectoEvent(IssueEvents.KeyFor(what), Fill(title, values), Fill(body, values))
        {
            UserId = issue.FirstReporter?.UserId,
            Url = "issues/#/item?id=" + issue.ItemId.ToString("N", CultureInfo.InvariantCulture),
            Level = what == NotificationEvent.IssueReported ? NectoEventLevel.Warning : NectoEventLevel.Info,
            Data = data
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

        // A requester reads that a failed request is tried again, never Radarr's or Sonarr's own error.
        var reason = what == NotificationEvent.Failed ? ServerWording.Get(TranslationKeys.RequestsNoteTriedAgain) : request.Note ?? string.Empty;
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
            ["reason"] = reason,
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
        var first = issue.FirstReporter?.UserId;
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
            ["user"] = issue.FirstReporter?.UserName ?? string.Empty,
            ["joined"] = Math.Max(0, issue.Reporters.Count - 1).ToString(CultureInfo.InvariantCulture),
            ["note"] = issue.Notes.FirstOrDefault(n => !n.Admin && n.UserId == first)?.Text ?? string.Empty,
            ["reason"] = issue.Notes.LastOrDefault(n => n.Admin)?.Text ?? string.Empty,
            ["decidedBy"] = issue.DecidedBy ?? string.Empty,
            ["issueType"] = issue.FirstReporter is { } reporter ? IssueService.TypeName(reporter.Type, language) : string.Empty,
            ["state"] = state.ToLowerInvariant()
        };
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

    // A copy taken now, so a change to the request before the send lands cannot change what is sent.
    private static MediaRequest Copy(MediaRequest request)
        => JsonSerializer.Deserialize<MediaRequest>(JsonSerializer.Serialize(request, CopyJson), CopyJson) ?? request;

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
