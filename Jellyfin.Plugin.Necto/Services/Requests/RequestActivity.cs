using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// The activity log entries that come in bursts, as the library fills requests or the status sync sends them again,
/// summed into one entry per outcome rather than one per request. One for the plugin, disposed with the server, which
/// writes whatever is still waiting.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class RequestActivity : IDisposable
{
    /// <summary>Requests the library filled.</summary>
    internal static readonly ActivityOutcome Available = new(
        RequestService.ActivityType + ".Available",
        TranslationKeys.RequestsActivitySummaryAvailableOne,
        TranslationKeys.RequestsActivitySummaryAvailableMany,
        (t, language) => Overview(t, language, TranslationKeys.RequestsActivitySummaryAvailableOverviewOne, TranslationKeys.RequestsActivitySummaryAvailableOverviewMany));

    /// <summary>Filled requests whose title, or some of it, left the library.</summary>
    internal static readonly ActivityOutcome Unavailable = new(
        RequestService.ActivityType + ".Unavailable",
        TranslationKeys.RequestsActivitySummaryUnavailableOne,
        TranslationKeys.RequestsActivitySummaryUnavailableMany,
        (t, language) => Overview(t, language, TranslationKeys.RequestsActivitySummaryUnavailableOverviewOne, TranslationKeys.RequestsActivitySummaryUnavailableOverviewMany));

    /// <summary>Failed requests the status sync sent again.</summary>
    internal static readonly ActivityOutcome SentOnRetry = new(
        RequestService.ActivityType + ".Approved",
        TranslationKeys.RequestsActivitySummarySentOnRetryOne,
        TranslationKeys.RequestsActivitySummarySentOnRetryMany,
        (t, language) => Overview(t, language, TranslationKeys.RequestsActivitySummarySentOnRetryOverviewOne, TranslationKeys.RequestsActivitySummarySentOnRetryOverviewMany));

    /// <summary>Approved requests Radarr or Sonarr refused, or that could not reach them.</summary>
    internal static readonly ActivityOutcome NotSent = new(
        RequestService.ActivityType + ".Failed",
        TranslationKeys.RequestsActivitySummaryNotSentOne,
        TranslationKeys.RequestsActivitySummaryNotSentMany,
        (t, language) => Overview(t, language, TranslationKeys.RequestsActivitySummaryNotSentOverviewOne, TranslationKeys.RequestsActivitySummaryNotSentOverviewMany),
        LogLevel.Warning);

    private readonly ActivitySummary _summary;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestActivity"/> class.
    /// </summary>
    /// <param name="logger">The activity log writer.</param>
    public RequestActivity(ActivityLogger logger)
    {
        _summary = new ActivitySummary(logger, language: () => ServerWording.Language);
    }

    /// <summary>Counts one request toward its outcome's next summary.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="source">Who or where it is for, such as the requester or the server.</param>
    /// <param name="reason">Why, when the outcome lists reasons.</param>
    public void Add(ActivityOutcome outcome, string source, string? reason = null)
        => _summary.Add(outcome, string.IsNullOrWhiteSpace(source) ? "unknown" : source, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim().TrimEnd('.'));

    /// <summary>The name of a run that decided several requests at once, as a bulk action or the automatic decline, in
    /// the server's display language.</summary>
    /// <param name="action">What was done: approve, decline, or remove.</param>
    /// <param name="count">How many requests it decided.</param>
    /// <returns>The name.</returns>
    internal static string RunName(string action, int count)
    {
        var (one, many) = action switch
        {
            "approve" => (TranslationKeys.RequestsActivityApprovedName, TranslationKeys.RequestsActivityRunApprovedMany),
            "decline" => (TranslationKeys.RequestsActivityRejectedName, TranslationKeys.RequestsActivityRunRejectedMany),
            _ => (TranslationKeys.RequestsActivityRemovedName, TranslationKeys.RequestsActivityRunRemovedMany)
        };
        return ServerWording.Get(count == 1 ? one : many);
    }

    /// <summary>The overview of a run that decided several requests at once, in the server's display language.</summary>
    /// <param name="action">What was done: approve, decline, or remove.</param>
    /// <param name="by">Who did it.</param>
    /// <param name="requesters">Each request's requester, one per request.</param>
    /// <param name="skipped">How many it could not apply to.</param>
    /// <returns>The overview.</returns>
    internal static string RunOverview(string action, string by, IReadOnlyList<string> requesters, int skipped = 0)
    {
        ArgumentNullException.ThrowIfNull(requesters);
        var (one, many) = action switch
        {
            "approve" => (TranslationKeys.RequestsActivityRunApprovedOverviewOne, TranslationKeys.RequestsActivityRunApprovedOverviewMany),
            "decline" => (TranslationKeys.RequestsActivityRunRejectedOverviewOne, TranslationKeys.RequestsActivityRunRejectedOverviewMany),
            _ => (TranslationKeys.RequestsActivityRunRemovedOverviewOne, TranslationKeys.RequestsActivityRunRemovedOverviewMany)
        };
        return RunEnding(ServerWording.Format(requesters.Count == 1 ? one : many, requesters.Count, by, Requesters(requesters)), skipped);
    }

    /// <summary>The overview of the automatic decline of requests left waiting too long, in the server's display language.</summary>
    /// <param name="days">How many days they waited.</param>
    /// <param name="requesters">Each request's requester, one per request.</param>
    /// <returns>The overview.</returns>
    internal static string ExpiredOverview(int days, IReadOnlyList<string> requesters)
    {
        ArgumentNullException.ThrowIfNull(requesters);
        var waited = days == 1 ? ServerWording.Get(TranslationKeys.RequestsDayCountOne) : ServerWording.Format(TranslationKeys.RequestsDayCount, days);
        var key = requesters.Count == 1 ? TranslationKeys.RequestsActivityRunExpiredOverviewOne : TranslationKeys.RequestsActivityRunExpiredOverviewMany;
        return RunEnding(ServerWording.Format(key, requesters.Count, waited, Requesters(requesters)), 0);
    }

    /// <summary>Writes every summary waiting, now.</summary>
    public void Flush() => _summary.Flush();

    /// <inheritdoc />
    public void Dispose() => _summary.Dispose();

    private static string Overview(ActivityTally tally, string language, string one, string many)
    {
        var text = StolonTranslations.Format(tally.Total == 1 ? one : many, language, tally.Total, Listed(tally.Sources));
        return tally.Reasons.Count == 0 ? text : StolonTranslations.Format(TranslationKeys.RequestsTagValue, language, text, Listed(tally.Reasons));
    }

    private static string Requesters(IReadOnlyList<string> requesters)
        => Listed(requesters.GroupBy(r => string.IsNullOrWhiteSpace(r) ? "unknown" : r, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));

    // Stolon's CountedList writes "(1)" after a name counted once, which the logging guide leaves out.
    private static string Listed(IReadOnlyDictionary<string, int> counts)
        => string.Join(", ", counts
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value == 1 ? p.Key : string.Create(CultureInfo.InvariantCulture, $"{p.Key} ({p.Value})")));

    private static string RunEnding(string text, int skipped)
    {
        if (skipped > 0)
        {
            text = skipped == 1 ? ServerWording.Format(TranslationKeys.RequestsActivityRunSkippedOne, text) : ServerWording.Format(TranslationKeys.RequestsActivityRunSkippedMany, text, skipped);
        }

        return ActivityText.Sentence(text)!;
    }
}
