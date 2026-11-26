using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Collects the users one scheduled run changed, so the run writes one activity entry naming them all instead of one
/// entry for each user.
/// </summary>
public sealed class UserRunTally
{
    private readonly List<string> _sources = new();
    private readonly List<string> _usernames = new();
    private Guid _lastUserId;

    /// <summary>Gets how many users were counted.</summary>
    public int Count => _usernames.Count;

    /// <summary>
    /// Counts one user.
    /// </summary>
    /// <param name="source">Where the change came from, such as the user's groups, as it reads in the overview.</param>
    /// <param name="username">The user's name.</param>
    /// <param name="userId">The user's id, attached to the entry when only one user changed.</param>
    public void Add(string source, string username, Guid userId = default)
    {
        if (!_sources.Contains(source, StringComparer.Ordinal))
        {
            _sources.Add(source);
        }

        _usernames.Add(username);
        _lastUserId = userId;
    }

    /// <summary>
    /// Builds the entry's name, such as "Users disabled".
    /// </summary>
    /// <param name="wording">The keys the entry reads from.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The name.</returns>
    public string Name(UserRunWording wording, string? language)
    {
        ArgumentNullException.ThrowIfNull(wording);
        return StolonTranslations.Get(Count == 1 ? wording.NameOne : wording.NameMany, language);
    }

    /// <summary>
    /// Builds the entry's overview, such as "Disabled 2 users when their group expired in Kids: viewer, guest".
    /// </summary>
    /// <param name="wording">The keys the entry reads from.</param>
    /// <param name="language">The language, or <c>null</c> for US English.</param>
    /// <returns>The overview.</returns>
    public string Overview(UserRunWording wording, string? language)
    {
        ArgumentNullException.ThrowIfNull(wording);
        return ActivityText.Sentence(StolonTranslations.Format(
            Count == 1 ? wording.OverviewOne : wording.OverviewMany,
            language,
            Count,
            string.Join(", ", _sources),
            string.Join(", ", _usernames.OrderBy(u => u, StringComparer.OrdinalIgnoreCase)))) ?? string.Empty;
    }

    /// <summary>
    /// Writes the run's one entry, when anything was counted.
    /// </summary>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="wording">The keys the entry reads from.</param>
    /// <param name="type">The activity type.</param>
    /// <param name="language">The server's language.</param>
    /// <param name="attachUser">Whether to attach the user's id when only one user changed.</param>
    public void Log(ActivityLogger activity, UserRunWording wording, string type, string? language, bool attachUser = true)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (Count == 0)
        {
            return;
        }

        activity.Log(Name(wording, language), type, Overview(wording, language), LogLevel.Warning, attachUser && Count == 1 ? _lastUserId : default);
    }
}
