using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Siphonophore.Stolon;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// The Scripts tool's summed activity entries, and its failures sent to Notifications. A script set off by a busy
/// event, such as items being added during a library scan, can fail many times in a row, so failures are summed into
/// one entry rather than one each, and each script sends at most one failure event a minute. Registered as a
/// singleton, so whatever is waiting is written when the server shuts down.
/// </summary>
public sealed class ScriptsActivity : IDisposable
{
    private readonly INotifier? _notifier;
    private readonly Func<string> _language;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, DateTime> _lastSent = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the outcome of script runs that ended with a failed step, by script name, with each first failure's message.
    /// Its name and overview are written in the language the summary is given.
    /// </summary>
    public static ActivityOutcome RunFailed { get; } = new(
        "Scripts.ScriptFailed",
        TranslationKeys.ScriptsActivityNameRunFailedOne,
        TranslationKeys.ScriptsActivityNameRunFailedMany,
        RunFailedOverview,
        LogLevel.Error);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class, writing in the server's display language.
    /// </summary>
    /// <param name="activityLogger">The activity log writer.</param>
    /// <param name="config">The server configuration, read for its display language each time an entry is written.</param>
    /// <param name="notifier">Sends each failure to Notifications.</param>
    public ScriptsActivity(ActivityLogger activityLogger, IServerConfigurationManager config, INotifier notifier)
        : this(new ActivitySummary(activityLogger, language: () => StolonTranslations.ServerLanguage(config)), notifier, () => StolonTranslations.ServerLanguage(config))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class, writing in US English.
    /// </summary>
    /// <param name="activityLogger">The activity log writer.</param>
    public ScriptsActivity(ActivityLogger activityLogger)
        : this(new ActivitySummary(activityLogger))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptsActivity"/> class around a given summary, for tests.
    /// </summary>
    /// <param name="summary">The summary entries are counted into.</param>
    /// <param name="notifier">Sends each failure to Notifications, or null to send none.</param>
    /// <param name="language">Gives the language of the events, or null for US English.</param>
    /// <param name="now">The clock that spaces the events, or null for the system clock.</param>
    public ScriptsActivity(ActivitySummary summary, INotifier? notifier = null, Func<string>? language = null, Func<DateTime>? now = null)
    {
        Summary = summary;
        _notifier = notifier;
        _language = language ?? (() => StolonTranslations.FallbackLanguage);
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>The shortest time between two failure events of one script.</summary>
    public static readonly TimeSpan EventSpacing = TimeSpan.FromMinutes(1);

    /// <summary>Gets the summary entries are counted into.</summary>
    public ActivitySummary Summary { get; }

    /// <summary>Counts one failed run toward the next summary.</summary>
    /// <param name="scriptName">The script's name.</param>
    /// <param name="message">The first failed step's message.</param>
    public void RunFailedFor(string scriptName, string? message)
    {
        var reason = ActivityText.Sentence(message);
        Summary.Add(RunFailed, scriptName, string.IsNullOrWhiteSpace(reason) ? null : reason);
        if (_notifier is null)
        {
            return;
        }

        var now = _now();
        lock (_lastSent)
        {
            if (_lastSent.TryGetValue(scriptName, out var last) && now - last < EventSpacing)
            {
                return;
            }

            _lastSent[scriptName] = now;
        }

        var language = _language();
        var body = string.IsNullOrWhiteSpace(reason)
            ? StolonTranslations.Format(TranslationKeys.ScheduledTaskFailedWithName, language, scriptName)
            : StolonTranslations.Format(TranslationKeys.ScriptsActivityRunFailedWithErrors, language, scriptName, reason);
        _ = _notifier.SendAsync(new NectoEvent(ScriptsEvents.Failed, StolonTranslations.Get(TranslationKeys.ScriptsActivityNameRunFailedOne, language), body)
        {
            Level = NectoEventLevel.Error,
            Data = new Dictionary<string, string> { ["script"] = scriptName, ["error"] = reason ?? string.Empty }
        });
    }

    /// <inheritdoc />
    public void Dispose() => Summary.Dispose();

    // A single run names its script and error. A larger summary counts the runs and lists each script and error.
    private static string RunFailedOverview(ActivityTally tally, string language)
    {
        var scripts = NamedList(tally.Sources, tally.Total);
        var errors = NamedList(tally.Reasons, tally.Total);
        if (tally.Total == 1)
        {
            return errors.Length > 0
                ? StolonTranslations.Format(TranslationKeys.ScriptsActivityRunFailedWithErrors, language, scripts, errors)
                : StolonTranslations.Format(TranslationKeys.ScheduledTaskFailedWithName, language, scripts);
        }

        return errors.Length > 0
            ? StolonTranslations.Format(TranslationKeys.ScriptsActivityRunsFailedWithErrors, language, tally.Total, scripts, errors)
            : StolonTranslations.Format(TranslationKeys.ScriptsActivityRunsFailed, language, tally.Total, scripts);
    }

    // Lists names, the largest count first. The count is left out when it is 1 or the whole total, since it
    // would then repeat what the sentence already says, so one script reads "Notify" rather than "Notify (1)".
    private static string NamedList(IReadOnlyDictionary<string, int> counts, int total)
        => string.Join(", ", counts
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value == 1 || p.Value == total
                ? p.Key
                : string.Create(CultureInfo.InvariantCulture, $"{p.Key} ({p.Value})")));
}
