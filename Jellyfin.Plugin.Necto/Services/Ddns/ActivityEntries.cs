using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Siphonophore.Stolon;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// The summed activity entries the plugin writes: one entry per outcome, such as "DNS records updated",
/// instead of one per record, ban or connection, in the server's display language. Each outcome's source is what the
/// count is listed by.
/// </summary>
public static class ActivityEntries
{
    /// <summary>Gets the bans past the few a minute written one by one, listed by country.</summary>
    public static ActivityOutcome ClientsBanned { get; } = new(
        "Necto.Proxy.AddressBanned",
        TranslationKeys.DdnsActivityClientsBannedOne,
        TranslationKeys.DdnsActivityClientsBannedMany,
        (t, language) => t.Total == 1
            ? StolonTranslations.Format(TranslationKeys.DdnsActivityClientsBannedOverviewOne, language, ListCounts(t.Sources))
            : StolonTranslations.Format(TranslationKeys.DdnsActivityClientsBannedOverviewMany, language, t.Total, ListCounts(t.Sources)),
        LogLevel.Warning);

    /// <summary>Gets the DNS records a run pushed a new address to, listed by hostname and address.</summary>
    public static ActivityOutcome RecordsUpdated { get; } = new(
        "Necto.Ddns.RecordUpdated",
        TranslationKeys.DdnsActivityRecordsUpdatedOne,
        TranslationKeys.DdnsActivityRecordsUpdatedMany,
        (t, language) => Overview(t, language, TranslationKeys.DdnsActivityRecordsUpdatedOverview, TranslationKeys.DdnsActivityRecordsUpdatedToOverview));

    /// <summary>Gets the DNS records that updated again after failing, listed by hostname and failed attempts.</summary>
    public static ActivityOutcome RecordsRecovered { get; } = new(
        "Necto.Ddns.RecordRecovered",
        TranslationKeys.DdnsActivityRecordsRecoveredOne,
        TranslationKeys.DdnsActivityRecordsRecoveredMany,
        (t, language) => Overview(t, language, TranslationKeys.DdnsActivityRecordsRecoveredOverview, TranslationKeys.DdnsActivityRecordsRecoveredAfterOverview));

    /// <summary>Gets the DNS records whose update failed and left DNS on an old address, listed by hostname and reason.</summary>
    public static ActivityOutcome RecordsFailed { get; } = new(
        "Necto.Ddns.RecordFailed",
        TranslationKeys.DdnsActivityRecordsFailedOne,
        TranslationKeys.DdnsActivityRecordsFailedMany,
        (t, language) => Overview(t, language, TranslationKeys.DdnsActivityRecordsFailedOverview, TranslationKeys.DdnsActivityRecordsFailedWhyOverview),
        LogLevel.Error);

    /// <summary>Gets the DNS records whose update failed while DNS already serves the current address.</summary>
    public static ActivityOutcome RecordsFailedServing { get; } = new(
        "Necto.Ddns.RecordFailed",
        TranslationKeys.DdnsActivityRecordsFailedOne,
        TranslationKeys.DdnsActivityRecordsFailedMany,
        (t, language) => Overview(t, language, TranslationKeys.DdnsActivityRecordsFailedServingOverview, TranslationKeys.DdnsActivityRecordsFailedServingWhyOverview),
        LogLevel.Warning);

    /// <summary>Gets the DNS records a run paused after repeated failures, listed by hostname and why.</summary>
    public static ActivityOutcome RecordsPaused { get; } = new(
        "Necto.Ddns.RecordBackoff",
        TranslationKeys.DdnsActivityRecordsPausedOne,
        TranslationKeys.DdnsActivityRecordsPausedMany,
        (t, language) => Overview(t, language, TranslationKeys.DdnsActivityRecordsPausedOverview, TranslationKeys.DdnsActivityRecordsPausedAfterOverview),
        LogLevel.Warning);

    /// <summary>The DNS records a save added, listed by hostname and provider.</summary>
    /// <param name="user">The name of the administrator who saved.</param>
    /// <returns>The outcome.</returns>
    public static ActivityOutcome RecordsAdded(string user) => new(
        "Necto.Ddns.RecordAdded",
        TranslationKeys.DdnsActivityRecordsAddedOne,
        TranslationKeys.DdnsActivityRecordsAddedMany,
        (t, language) => UserOverview(t, language, user, TranslationKeys.DdnsActivityRecordsAddedOverview, TranslationKeys.DdnsActivityRecordsAddedWithOverview));

    /// <summary>The DNS records a save changed, listed by hostname and whether each is on.</summary>
    /// <param name="user">The name of the administrator who saved.</param>
    /// <returns>The outcome.</returns>
    public static ActivityOutcome RecordsChanged(string user) => new(
        "Necto.Ddns.RecordChanged",
        TranslationKeys.DdnsActivityRecordsChangedOne,
        TranslationKeys.DdnsActivityRecordsChangedMany,
        (t, language) => UserOverview(t, language, user, TranslationKeys.DdnsActivityRecordsChangedOverview, TranslationKeys.DdnsActivityRecordsChangedToOverview));

    /// <summary>The DNS records a save removed, listed by hostname and provider.</summary>
    /// <param name="user">The name of the administrator who saved.</param>
    /// <returns>The outcome.</returns>
    public static ActivityOutcome RecordsRemoved(string user) => new(
        "Necto.Ddns.RecordRemoved",
        TranslationKeys.DdnsActivityRecordsRemovedOne,
        TranslationKeys.DdnsActivityRecordsRemovedMany,
        (t, language) => UserOverview(t, language, user, TranslationKeys.DdnsActivityRecordsRemovedOverview, TranslationKeys.DdnsActivityRecordsRemovedWithOverview));

    /// <summary>Gets the Let's Encrypt TXT records a cleanup gave up removing, listed by name.</summary>
    public static ActivityOutcome TxtRecordsLeftOver { get; } = new(
        "Necto.Proxy.AcmeTxtLeftOver",
        TranslationKeys.DdnsActivityTxtRecordsLeftOverOne,
        TranslationKeys.DdnsActivityTxtRecordsLeftOverMany,
        (t, language) => StolonTranslations.Format(TranslationKeys.DdnsActivityTxtRecordsLeftOverOverview, language, ListCounts(t.Sources)),
        LogLevel.Warning);

    /// <summary>
    /// The hourly entry for refused internet connections, in the server's display language, as "Connections
    /// refused" over "Refused 12 internet connections in the last hour by the country rules (9), bans (3)".
    /// </summary>
    /// <param name="totals">The hour's refusals.</param>
    /// <returns>The name and overview.</returns>
    public static (string Name, string Overview) Refused(RefusalTotals totals)
    {
        var language = ServerText.Language;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [StolonTranslations.Get(TranslationKeys.DdnsActivityRefusedByCountryRules, language)] = Clamp(totals.Country),
            [StolonTranslations.Get(TranslationKeys.DdnsActivityRefusedByBans, language)] = Clamp(totals.Ban),
            [StolonTranslations.Get(TranslationKeys.DdnsActivityRefusedByBlockList, language)] = Clamp(totals.Address),
            [StolonTranslations.Get(TranslationKeys.DdnsActivityRefusedByLimits, language)] = Clamp(totals.Limit)
        };
        var total = Clamp(totals.Total);
        var name = StolonTranslations.Get(total == 1 ? TranslationKeys.DdnsActivityConnectionsRefusedOne : TranslationKeys.DdnsActivityConnectionsRefusedMany, language);
        var overview = total == 1
            ? StolonTranslations.Format(TranslationKeys.DdnsActivityConnectionsRefusedOverviewOne, language, ListCounts(reasons))
            : StolonTranslations.Format(TranslationKeys.DdnsActivityConnectionsRefusedOverviewMany, language, total, ListCounts(reasons));
        return (name, ActivityText.Sentence(overview)!);
    }

    /// <summary>
    /// Outside text, such as an error or a provider reply, as one plain sentence: trimmed, capitalized and
    /// with no closing full stop, cut to what the activity log keeps.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The sentence, empty for blank text.</returns>
    public static string Plain(string? text) => ActivityLogger.Trim(ActivityText.Sentence(text?.Trim()) ?? string.Empty);

    /// <summary>Outside text as a phrase inside a sentence: trimmed, with no closing full stop.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The phrase.</returns>
    public static string Phrase(string? text) => (text ?? string.Empty).Trim().TrimEnd('.');

    /// <summary>
    /// Lists names with their counts, the largest first, such as "a.example.com, b.example.com (3)". Unlike Base's
    /// <see cref="ActivityText.CountedList"/> it leaves out a count of 1, as the activity log writes it.
    /// </summary>
    /// <param name="counts">The counts by name. A count of zero or less is left out.</param>
    /// <returns>The list, or an empty string when there is nothing to list.</returns>
    public static string ListCounts(IReadOnlyDictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return string.Join(", ", counts
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value == 1 ? p.Key : string.Create(CultureInfo.InvariantCulture, $"{p.Key} ({p.Value})")));
    }

    // The overview that lists the sources, with the reasons when any were given.
    private static string Overview(ActivityTally tally, string language, string plain, string withReasons)
        => tally.Reasons.Count == 0
            ? StolonTranslations.Format(plain, language, ListCounts(tally.Sources))
            : StolonTranslations.Format(withReasons, language, ListCounts(tally.Sources), ListCounts(tally.Reasons));

    // The same overview, led by the administrator who made the change.
    private static string UserOverview(ActivityTally tally, string language, string user, string plain, string withReasons)
        => tally.Reasons.Count == 0
            ? StolonTranslations.Format(plain, language, user, ListCounts(tally.Sources))
            : StolonTranslations.Format(withReasons, language, user, ListCounts(tally.Sources), ListCounts(tally.Reasons));

    private static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
