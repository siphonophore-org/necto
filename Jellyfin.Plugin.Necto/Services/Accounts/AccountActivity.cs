using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// The summed account entries in the server's language. Users moved onto or off password rules in a burst, such as
/// when a group save applies to every member, are written as one entry per outcome.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class AccountActivity : IDisposable
{
    /// <summary>Users moved onto password rules, counted by username.</summary>
    public static readonly ActivityOutcome PasswordRulesEnrolled = new(
        "Necto.PasswordRulesEnrolled",
        TranslationKeys.ActivityRulesEnrolledOne,
        TranslationKeys.ActivityRulesEnrolledMany,
        (t, language) => StolonTranslations.Format(
            t.Total == 1 ? TranslationKeys.ActivityRulesEnrolledOverviewOne : TranslationKeys.ActivityRulesEnrolledOverviewMany,
            language,
            t.Total,
            Names(t.Sources)));

    /// <summary>Users moved back to the provider they had before, counted by username.</summary>
    public static readonly ActivityOutcome PasswordRulesRemoved = new(
        "Necto.PasswordRulesRemoved",
        TranslationKeys.ActivityRulesRemovedOne,
        TranslationKeys.ActivityRulesRemovedMany,
        (t, language) => StolonTranslations.Format(
            t.Total == 1 ? TranslationKeys.ActivityRulesRemovedOverviewOne : TranslationKeys.ActivityRulesRemovedOverviewMany,
            language,
            t.Total,
            Names(t.Sources)));

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountActivity"/> class.
    /// </summary>
    /// <param name="logger">Writes the activity log.</param>
    /// <param name="serverConfig">Gives the server's language.</param>
    public AccountActivity(ActivityLogger logger, IServerConfigurationManager serverConfig)
        : this(new ActivitySummary(logger, language: () => StolonTranslations.ServerLanguage(serverConfig)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccountActivity"/> class over a summary.
    /// </summary>
    /// <param name="summary">Sums and writes the entries.</param>
    public AccountActivity(ActivitySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
    }

    /// <summary>Gets the summary that sums and writes the entries.</summary>
    public ActivitySummary Summary { get; }

    /// <summary>
    /// Counts one event toward its outcome's next entry.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="username">The user it happened to.</param>
    public void Add(ActivityOutcome outcome, string username) => Summary.Add(outcome, username);

    /// <inheritdoc />
    public void Dispose() => Summary.Dispose();

    /// <summary>
    /// Lists the counted names in order, such as "guest, viewer".
    /// </summary>
    /// <param name="sources">The counts by name.</param>
    /// <returns>The list.</returns>
    public static string Names(IReadOnlyDictionary<string, int> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return string.Join(", ", sources.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
    }
}
