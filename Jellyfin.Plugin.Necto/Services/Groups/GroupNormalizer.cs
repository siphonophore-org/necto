using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Brings the groups back to what Necto relies on: Everyone exists and holds no members, every group has an id and
/// every section, no member is listed twice, administrators and deleted users hold no membership, and every setting
/// that names a group names one that exists.
/// </summary>
public static class GroupNormalizer
{
    /// <summary>
    /// Normalizes the groups in place.
    /// </summary>
    /// <param name="config">The configuration to normalize.</param>
    /// <param name="knownUsers">Every user on the server, or <c>null</c> when the list cannot be read. An empty list counts as unreadable, so a pass can never wipe membership.</param>
    /// <param name="administrators">The administrators on the server, or <c>null</c> when unknown.</param>
    /// <returns><c>true</c> when something changed.</returns>
    public static bool Normalize(PluginConfiguration config, IReadOnlyCollection<Guid>? knownUsers = null, IReadOnlyCollection<Guid>? administrators = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var before = Snapshot(config);

        config.Groups ??= new List<Group>();
        config.GroupSettings ??= new GroupSettings();
        config.AccountSettings ??= new AccountSettings();
        config.ProviderEnrollments ??= new List<ProviderEnrollment>();

        var everyone = config.Everyone ?? config.Groups.FirstOrDefault(g => g?.Id == Group.EveryoneId) ?? PluginConfiguration.NewEveryone();
        everyone.Id = Group.EveryoneId;
        everyone.Name = PluginConfiguration.NewEveryone().Name;
        everyone.MemberIds = new List<Guid>();
        Complete(everyone);
        config.Everyone = everyone;

        var seen = new HashSet<Guid>();
        var kept = new List<Group>();
        foreach (var group in config.Groups)
        {
            if (group is null || group.Id == Group.EveryoneId)
            {
                continue;
            }

            if (group.Id == Guid.Empty)
            {
                group.Id = Guid.NewGuid();
            }

            if (!seen.Add(group.Id))
            {
                continue;
            }

            group.Name = (group.Name ?? string.Empty).Trim();
            Complete(group);
            group.MemberIds = group.MemberIds.Where(id => id != Guid.Empty).Distinct().ToList();
            kept.Add(group);
        }

        config.Groups = kept;

        var known = knownUsers is { Count: > 0 } ? knownUsers.ToHashSet() : null;
        var admins = administrators?.ToHashSet() ?? new HashSet<Guid>();
        foreach (var group in config.Groups)
        {
            // Administrators are exempt from every group, so a promoted member is only dead weight in the list.
            group.MemberIds.RemoveAll(id => admins.Contains(id) || (known is not null && !known.Contains(id)));
        }

        // An administrator's record stays, since it puts them back on the provider they had before.
        if (known is not null)
        {
            config.ProviderEnrollments.RemoveAll(e => e is null || !known.Contains(e.UserId));
        }

        var ids = config.Groups.Select(g => g.Id).Append(Group.EveryoneId).ToHashSet();
        if (config.GroupSettings.NewUserGroupId is { } newUserGroup && (newUserGroup == Group.EveryoneId || !ids.Contains(newUserGroup)))
        {
            config.GroupSettings.NewUserGroupId = null;
        }

        foreach (var group in config.Groups.Append(config.Everyone))
        {
            group.Permissions.Invites.TargetGroupIds = group.Permissions.Invites.TargetGroupIds.Where(ids.Contains).Distinct().ToList();
        }

        Invites.InviteNormalizer.Normalize(config);

        return !string.Equals(before, Snapshot(config), StringComparison.Ordinal);
    }

    /// <summary>
    /// Fills every section a group is missing, such as after a post that left one out.
    /// </summary>
    /// <param name="group">The group to complete.</param>
    public static void Complete(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.Name ??= string.Empty;
        group.MemberIds ??= new List<Guid>();
        var p = group.Permissions ??= new GroupPermissions();
        var accounts = p.Accounts ??= new AccountRules();
        accounts.Password ??= new PasswordRules();
        accounts.Expiry ??= new ExpiryRule();
        accounts.Inactivity ??= new InactivityRule();
        var sessions = accounts.Sessions ??= new SessionRules();
        sessions.Rules = (sessions.Rules ?? new List<SessionCleanupRule>()).Where(r => r is not null).ToList();
        foreach (var rule in sessions.Rules)
        {
            rule.Clients = (rule.Clients ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        p.Requests ??= new RequestPermissions();
        p.Issues ??= new IssuePermissions();
        var invites = p.Invites ??= new InvitePermissions();
        invites.TargetGroupIds ??= new List<Guid>();
        p.WireGuard ??= new WireGuardPermissions();
        var scripts = p.Scripts ??= new ScriptPermissions();
        scripts.ScriptIds = Clean(scripts.ScriptIds);
        var pages = p.CustomPages ??= new CustomPagePermissions();
        pages.PageIds = Clean(pages.PageIds);
        var notifications = p.Notifications ??= new NotificationPermissions();
        notifications.EventKeys = Clean(notifications.EventKeys);
        Policies.PolicyTemplateNormalizer.Complete(group.Template ??= new Jellyfin.Plugin.Necto.Configuration.Policies.PolicyTemplate());
    }

    private static List<string> Clean(List<string>? values)
        => (values ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static string Snapshot(PluginConfiguration config)
        => System.Text.Json.JsonSerializer.Serialize(new { config.Groups, config.Everyone, config.GroupSettings, config.AccountSettings, config.ProviderEnrollments });
}
