using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Notifications;
using MediaBrowser.Controller.Configuration;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Writes store entries to Jellyfin's activity log and sends them to Notifications. Records added and removed are
/// summed into one entry per outcome once a burst goes quiet, so a busy form cannot bury everything else. A cleared
/// store is one administrator action and goes out at once. Every entry is in the server's language.
/// </summary>
public sealed class StoreActivity : IDisposable
{
    /// <summary>The activity type of a store cleared by an administrator.</summary>
    public const string ClearedType = "Necto.PagesStoreCleared";

    private readonly ActivityLogger _logger;
    private readonly ActivitySummary _summary;
    private readonly Func<string?> _language;
    private readonly INotifier? _notifier;
    private readonly ActivityOutcome _added;
    private readonly ActivityOutcome _removed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class.
    /// </summary>
    /// <param name="logger">Writes the activity log.</param>
    /// <param name="config">Gives the server's language.</param>
    /// <param name="notifier">Sends each entry to Notifications.</param>
    public StoreActivity(ActivityLogger logger, IServerConfigurationManager config, INotifier notifier)
        : this(logger, () => StolonTranslations.ServerLanguage(config), notifier, null)
    {
        ArgumentNullException.ThrowIfNull(config);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreActivity"/> class with its language and its own summary
    /// timing, so a test chooses when it writes and in what.
    /// </summary>
    /// <param name="logger">Writes the activity log.</param>
    /// <param name="language">Gives the language of the entries, or <c>null</c> for US English.</param>
    /// <param name="notifier">Sends each entry to Notifications, or <c>null</c> to send none.</param>
    /// <param name="quiet">How long a burst must go quiet before its entry is written, or <c>null</c> for the default.</param>
    internal StoreActivity(ActivityLogger logger, Func<string?> language, INotifier? notifier, TimeSpan? quiet)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(language);
        _logger = logger;
        _language = language;
        _notifier = notifier;
        _summary = new ActivitySummary(logger, quiet: quiet, language: language);
        _added = new ActivityOutcome(
            "Necto.PagesRecordsAdded",
            TranslationKeys.PagesActivityAddedOne,
            TranslationKeys.PagesActivityAddedMany,
            (tally, lang) => Announce(CustomPageEvents.RecordsAdded, TranslationKeys.PagesActivityAddedOne, TranslationKeys.PagesActivityAddedMany, tally, AddedOverview(tally, lang), lang));
        _removed = new ActivityOutcome(
            "Necto.PagesRecordsRemoved",
            TranslationKeys.PagesActivityRemovedOne,
            TranslationKeys.PagesActivityRemovedMany,
            (tally, lang) => Announce(CustomPageEvents.RecordsRemoved, TranslationKeys.PagesActivityRemovedOne, TranslationKeys.PagesActivityRemovedMany, tally, RemovedOverview(tally, lang), lang));
    }

    /// <summary>Counts a record written to a store.</summary>
    /// <param name="store">The store's name.</param>
    /// <param name="label">The record's label, when the page gave one.</param>
    public void RecordAdded(string store, string? label) => _summary.Add(_added, store, label);

    /// <summary>Counts a record deleted from a store.</summary>
    /// <param name="store">The store's name.</param>
    /// <param name="label">The record's label, when the page gave one.</param>
    public void RecordRemoved(string store, string? label) => _summary.Add(_removed, store, label);

    /// <summary>Writes the entry for a store an administrator cleared, and sends it.</summary>
    /// <param name="store">The store's name.</param>
    /// <param name="removed">How many records the clear removed.</param>
    /// <param name="userName">The administrator, or <c>null</c> for an API key.</param>
    public void StoreCleared(string store, int removed, string? userName = null)
    {
        var language = _language();
        var title = StolonTranslations.Get(TranslationKeys.PagesActivityStoreCleared, language);
        var overview = ClearedOverview(store, removed, userName, language);
        _logger.Log(title, ClearedType, overview);
        Send(new NectoEvent(CustomPageEvents.StoreCleared, title, overview)
        {
            Url = "web/#/configurationpage?name=necto",
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["store"] = store,
                ["count"] = removed.ToString(CultureInfo.InvariantCulture)
            }
        });
    }

    /// <summary>Writes every summed entry waiting, now.</summary>
    public void Flush() => _summary.Flush();

    /// <inheritdoc />
    public void Dispose() => _summary.Dispose();

    /// <summary>
    /// Writes the overview of records added, naming the stores and any labels.
    /// </summary>
    /// <param name="tally">What was counted.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string AddedOverview(ActivityTally tally, string? language = null)
        => Overview(tally, TranslationKeys.PagesActivityRecordAdded, TranslationKeys.PagesActivityRecordAddedNamed, TranslationKeys.PagesActivityRecordsAdded, language);

    /// <summary>
    /// Writes the overview of records removed, naming the stores and any labels.
    /// </summary>
    /// <param name="tally">What was counted.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string RemovedOverview(ActivityTally tally, string? language = null)
        => Overview(tally, TranslationKeys.PagesActivityRecordRemoved, TranslationKeys.PagesActivityRecordRemovedNamed, TranslationKeys.PagesActivityRecordsRemoved, language);

    /// <summary>
    /// Writes the overview of a cleared store, naming who cleared it when known.
    /// </summary>
    /// <param name="store">The store's name.</param>
    /// <param name="removed">How many records the clear removed.</param>
    /// <param name="userName">The administrator, or <c>null</c> for an API key.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public static string ClearedOverview(string store, int removed, string? userName = null, string? language = null)
    {
        var text = string.IsNullOrEmpty(userName)
            ? removed == 1
                ? StolonTranslations.Format(TranslationKeys.PagesActivityRecordCleared, language, store)
                : StolonTranslations.Format(TranslationKeys.PagesActivityRecordsCleared, language, removed, store)
            : removed == 1
                ? StolonTranslations.Format(TranslationKeys.PagesActivityRecordClearedBy, language, userName, store)
                : StolonTranslations.Format(TranslationKeys.PagesActivityRecordsClearedBy, language, userName, removed, store);
        return ActivityText.Sentence(text)!;
    }

    /// <summary>
    /// Sends a summed entry to Notifications as it is written, and gives its overview back to the activity log.
    /// </summary>
    private string Announce(string key, string one, string many, ActivityTally tally, string overview, string language)
    {
        var title = StolonTranslations.Format(tally.Total == 1 ? one : many, language, tally.Total);
        Send(new NectoEvent(key, title, overview)
        {
            Url = "web/#/configurationpage?name=necto",
            Data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["stores"] = string.Join(",", tally.Sources.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                ["count"] = tally.Total.ToString(CultureInfo.InvariantCulture)
            }
        });
        return overview;
    }

    /// <summary>
    /// Hands an event to Notifications without waiting, since an entry is written from a timer or a request.
    /// </summary>
    private void Send(NectoEvent nectoEvent)
    {
        if (_notifier is not null)
        {
            _ = SendAsync(_notifier, nectoEvent);
        }
    }

    /// <summary>
    /// Sends an event, keeping a failure from reaching the write that raised it.
    /// </summary>
    private static async Task SendAsync(INotifier notifier, NectoEvent nectoEvent)
    {
        try
        {
            await notifier.SendAsync(nectoEvent).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Notifications never throws for a channel, so anything left is not the store's to handle.
        }
    }

    /// <summary>
    /// Names the records. One labeled record is named in place of the count, and a larger summary lists any labels
    /// after the stores. Records with no label are counted but not named.
    /// </summary>
    private static string Overview(ActivityTally tally, string one, string named, string many, string? language)
    {
        ArgumentNullException.ThrowIfNull(tally);

        // One store holds every record counted, so its count would only repeat the total.
        var stores = tally.Sources.Count == 1 ? tally.Sources.Keys.First() : NamedList(tally.Sources);
        var labels = NamedList(tally.Reasons);
        string text;
        if (tally.Total == 1)
        {
            text = labels.Length > 0
                ? StolonTranslations.Format(named, language, labels, stores)
                : StolonTranslations.Format(one, language, stores);
        }
        else
        {
            text = StolonTranslations.Format(many, language, tally.Total, stores);
            if (labels.Length > 0)
            {
                text = StolonTranslations.Format(TranslationKeys.PagesActivityIncluding, language, text, labels);
            }
        }

        return ActivityText.Sentence(text)!;
    }

    /// <summary>
    /// Lists names, the largest count first, with the count in brackets only above one.
    /// </summary>
    private static string NamedList(IReadOnlyDictionary<string, int> counts)
        => string.Join(", ", counts
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value == 1
                ? p.Key
                : string.Create(CultureInfo.InvariantCulture, $"{p.Key} ({p.Value})")));
}
