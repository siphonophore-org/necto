using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Works out which account rules hold for a user from their one group. Each rule holds while the group has it on, and a
/// user in no group takes Everyone's rules.
/// </summary>
public static class AccountRuleMerger
{
    /// <summary>
    /// Works out the password rules a user is held to.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <returns>A copy of the group's rules, or <c>null</c> when the group enforces none.</returns>
    public static PasswordRules? Password(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Permissions?.Accounts?.Password is not { Enabled: true } p)
        {
            return null;
        }

        return new PasswordRules
        {
            Enabled = true,
            DisallowEmpty = p.DisallowEmpty,
            MinLength = p.MinLength,
            RequireUppercase = p.RequireUppercase,
            RequireNumber = p.RequireNumber,
            RequireSymbol = p.RequireSymbol,
            ChangeMode = p.ChangeMode
        };
    }

    /// <summary>
    /// Works out the date a user's account expires on.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <returns>The group's date, or <c>null</c> when it has none.</returns>
    public static DateTime? ExpiresOn(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Permissions?.Accounts?.Expiry?.ExpiresOn?.Date;
    }

    /// <summary>
    /// Works out what happens to a user's account today because of expiry.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <param name="today">Today's date on the server.</param>
    /// <returns>The group's action once its date has come, or <c>null</c> before it or when it has none.</returns>
    public static ExpiryAction? Expiry(Group group, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (ExpiresOn(group) is not { } due || due > today.Date)
        {
            return null;
        }

        return ExpiryActionOf(group);
    }

    /// <summary>
    /// Works out what expiry does to a user's account once it comes.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <returns>Delete when the group deletes, Disable otherwise, or <c>null</c> when the account never expires.</returns>
    public static ExpiryAction? ExpiryActionOf(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (ExpiresOn(group) is null)
        {
            return null;
        }

        return group.Permissions.Accounts.Expiry.Action == ExpiryAction.Delete ? ExpiryAction.Delete : ExpiryAction.Disable;
    }

    /// <summary>
    /// Works out how many days without activity disable a user.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <returns>The group's window, or <c>null</c> when it leaves inactive members alone.</returns>
    public static int? InactiveDays(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Permissions?.Accounts?.Inactivity is { Enabled: true, Days: > 0 } rule ? rule.Days : null;
    }

    /// <summary>
    /// Works out how many days a user's device on a client may go without checking in.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <param name="appName">The client the device runs.</param>
    /// <returns>The shortest window of the group's rules for the client, or <c>null</c> when the group leaves it alone.</returns>
    public static int? SessionDays(Group group, string? appName)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Permissions?.Accounts?.Sessions is { Enabled: true } sessions
            ? MinMatchingDays(sessions.Rules, appName ?? string.Empty)
            : null;
    }

    /// <summary>
    /// Works out whether a user's group cleans up sessions.
    /// </summary>
    /// <param name="group">The group that applies to the user.</param>
    /// <returns><c>true</c> when the group has session cleanup on with a rule.</returns>
    public static bool CleansSessions(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Permissions?.Accounts?.Sessions is { Enabled: true } s && s.Rules.Count > 0;
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
