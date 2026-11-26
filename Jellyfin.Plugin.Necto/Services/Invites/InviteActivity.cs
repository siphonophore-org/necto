using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// The summed invite entries in the server's language. Wrong PINs that come in a burst are written as one entry.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class InviteActivity : IDisposable
{
    /// <summary>Wrong PINs, counted by invite.</summary>
    public static readonly ActivityOutcome PinFailed = new(
        "Necto.InvitePinFailed",
        TranslationKeys.InvitesActivityPinFailedOne,
        TranslationKeys.InvitesActivityPinFailedMany,
        (t, language) => StolonTranslations.Format(
            t.Total == 1 ? TranslationKeys.InvitesActivityPinFailedOverviewOne : TranslationKeys.InvitesActivityPinFailedOverviewMany,
            language,
            t.Total,
            Counted(t.Sources)),
        LogLevel.Warning);

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteActivity"/> class.
    /// </summary>
    /// <param name="logger">Writes the activity log.</param>
    /// <param name="serverConfig">Gives the server's language.</param>
    public InviteActivity(ActivityLogger logger, IServerConfigurationManager serverConfig)
        : this(new ActivitySummary(logger, language: () => StolonTranslations.ServerLanguage(serverConfig)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteActivity"/> class over a summary.
    /// </summary>
    /// <param name="summary">Sums and writes the entries.</param>
    public InviteActivity(ActivitySummary summary)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    /// <summary>Gets the summary that sums and writes the entries.</summary>
    public ActivitySummary Summary { get; }

    /// <summary>
    /// Counts one event toward its outcome's next entry.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="invite">The invite's name.</param>
    public void Add(ActivityOutcome outcome, string invite) => Summary.Add(outcome, invite);

    /// <inheritdoc />
    public void Dispose() => Summary.Dispose();

    /// <summary>
    /// Lists the counted invites, each with its count when above one, such as "Family (3), Friends".
    /// </summary>
    /// <param name="sources">The counts by invite.</param>
    /// <returns>The list.</returns>
    public static string Counted(IReadOnlyDictionary<string, int> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return string.Join(", ", sources.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Value > 1 ? p.Key + " (" + p.Value + ")" : p.Key));
    }
}
