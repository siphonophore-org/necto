using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Works out which account rules hold for a user from the groups that apply to them. A rule that ends an account,
/// such as expiry, inactivity, or session cleanup, holds only when every group has it, since any one group lets the
/// member stay, and its window is the largest any group gives. Password rules hold when any group enforces them, at
/// the strictest of those groups, and the change mode is the most generous of them.
/// </summary>
public static class AccountRuleMerger
{
    /// <summary>
    /// Works out the password rules a user is held to.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <returns>The merged rules, or <c>null</c> when no group enforces any.</returns>
    public static PasswordRules? Password(IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var enforcing = groups.Select(g => g.Permissions?.Accounts?.Password).Where(p => p is { Enabled: true }).Select(p => p!).ToList();
        if (enforcing.Count == 0)
        {
            return null;
        }

        return new PasswordRules
        {
            Enabled = true,
            DisallowEmpty = enforcing.Any(p => p.DisallowEmpty),
            MinLength = enforcing.Max(p => p.MinLength),
            RequireUppercase = enforcing.Any(p => p.RequireUppercase),
            RequireNumber = enforcing.Any(p => p.RequireNumber),
            RequireSymbol = enforcing.Any(p => p.RequireSymbol),
            ChangeMode = enforcing.Min(p => p.ChangeMode)
        };
    }

    /// <summary>
    /// Works out the date a user's account expires on.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <returns>The latest date of the groups, or <c>null</c> when any group has none.</returns>
    public static DateTime? ExpiresOn(IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var dates = groups.Select(g => g.Permissions?.Accounts?.Expiry?.ExpiresOn).ToList();
        if (dates.Count == 0 || dates.Any(d => d is null))
        {
            return null;
        }

        return dates.Max(d => d!.Value.Date);
    }

    /// <summary>
    /// Works out what happens to a user's account today because of expiry.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <param name="today">Today's date on the server.</param>
    /// <returns>Delete when every group has expired and deletes, Disable when every group has expired and any disables, or <c>null</c> while any group has not expired.</returns>
    public static ExpiryAction? Expiry(IReadOnlyList<Group> groups, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (ExpiresOn(groups) is not { } due || due > today.Date)
        {
            return null;
        }

        return ExpiryActionOf(groups);
    }

    /// <summary>
    /// Works out what expiry does to a user's account once it comes.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <returns>Delete when every group deletes, Disable otherwise, or <c>null</c> when the account never expires.</returns>
    public static ExpiryAction? ExpiryActionOf(IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (ExpiresOn(groups) is null)
        {
            return null;
        }

        // Delete cannot be undone, so it takes every group asking for it.
        return groups.All(g => g.Permissions.Accounts.Expiry.Action == ExpiryAction.Delete) ? ExpiryAction.Delete : ExpiryAction.Disable;
    }

    /// <summary>
    /// Works out how many days without activity disable a user.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <returns>The largest window of the groups, or <c>null</c> when any group leaves inactive members alone.</returns>
    public static int? InactiveDays(IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var rules = groups.Select(g => g.Permissions?.Accounts?.Inactivity).ToList();
        if (rules.Count == 0 || rules.Any(r => r is not { Enabled: true, Days: > 0 }))
        {
            return null;
        }

        return rules.Max(r => r!.Days);
    }

    /// <summary>
    /// Works out how many days a user's device on a client may go without checking in.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <param name="appName">The client the device runs.</param>
    /// <returns>The largest of the groups' windows for the client, or <c>null</c> when any group leaves the client alone.</returns>
    public static int? SessionDays(IReadOnlyList<Group> groups, string? appName)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            return null;
        }

        var largest = 0;
        foreach (var group in groups)
        {
            var sessions = group.Permissions?.Accounts?.Sessions;
            if (sessions is not { Enabled: true } || MinMatchingDays(sessions.Rules, appName ?? string.Empty) is not { } days)
            {
                return null;
            }

            largest = Math.Max(largest, days);
        }

        return largest;
    }

    /// <summary>
    /// Works out whether every group that applies to a user cleans up sessions.
    /// </summary>
    /// <param name="groups">The groups that apply to the user.</param>
    /// <returns><c>true</c> when every group has session cleanup on with a rule.</returns>
    public static bool CleansSessions(IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Count > 0 && groups.All(g => g.Permissions?.Accounts?.Sessions is { Enabled: true } s && s.Rules.Count > 0);
    }

    /// <summary>
    /// Works out whether a rule covers a client.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="appName">The client's name.</param>
    /// <returns><c>true</c> when the rule covers it.</returns>
    public static bool RuleMatches(SessionCleanupRule rule, string appName)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var listed = (rule.Clients ?? new List<string>()).Contains(appName, StringComparer.OrdinalIgnoreCase);
        return rule.Mode switch
        {
            SessionClientMode.All => true,
            SessionClientMode.Only => listed,
            SessionClientMode.AllExcept => !listed,
            _ => false
        };
    }

    /// <summary>
    /// Works out the shortest window among one group's rules that cover a client.
    /// </summary>
    /// <param name="rules">The group's rules.</param>
    /// <param name="appName">The client's name.</param>
    /// <returns>The shortest window, or <c>null</c> when no rule covers the client.</returns>
    public static int? MinMatchingDays(IEnumerable<SessionCleanupRule>? rules, string appName)
    {
        int? min = null;
        foreach (var rule in rules ?? Enumerable.Empty<SessionCleanupRule>())
        {
            if (rule.Days > 0 && RuleMatches(rule, appName) && (min is null || rule.Days < min))
            {
                min = rule.Days;
            }
        }

        return min;
    }
}
