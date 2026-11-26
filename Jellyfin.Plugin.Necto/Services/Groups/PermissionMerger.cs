using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Merges the permissions of the groups that apply to a user. An allow from any group allows, a list holds what any
/// group lists, and a count or limit takes the largest any allowing group gives, where 0 is no limit and so the largest.
/// </summary>
public static class PermissionMerger
{
    /// <summary>
    /// Merges the groups that apply to a user into what the user may do.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="groups">The groups that apply to the user, Everyone when the user is in no group.</param>
    /// <returns>The user's permissions.</returns>
    public static EffectivePermissions Merge(Guid userId, IReadOnlyList<Group> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var p = groups.Select(g => g.Permissions ?? new GroupPermissions()).ToList();

        var requesters = p.Where(x => x.Requests.MayRequest).ToList();
        var peerHolders = p.Where(x => x.WireGuard.MayHavePeers).ToList();
        var inviters = p.Where(x => x.Invites.MayCreate).ToList();

        return new EffectivePermissions
        {
            UserId = userId,
            GroupIds = groups.Select(g => g.Id).ToList(),
            Accounts = new EffectiveAccounts
            {
                Password = AccountRuleMerger.Password(groups),
                ExpiresOn = AccountRuleMerger.ExpiresOn(groups),
                ExpiryAction = AccountRuleMerger.ExpiryActionOf(groups),
                InactiveDays = AccountRuleMerger.InactiveDays(groups),
                CleansSessions = AccountRuleMerger.CleansSessions(groups)
            },
            Requests = new RequestPermissions
            {
                MayRequest = requesters.Count > 0,
                AutoApprove = p.Any(x => x.Requests.AutoApprove),
                MovieQuota = Largest(requesters.Select(x => x.Requests.MovieQuota)),
                SeriesQuota = Largest(requesters.Select(x => x.Requests.SeriesQuota)),
                MaySeeOthers = p.Any(x => x.Requests.MaySeeOthers),
                MayManage = p.Any(x => x.Requests.MayManage)
            },
            Issues = new IssuePermissions
            {
                MayReport = p.Any(x => x.Issues.MayReport),
                MayManage = p.Any(x => x.Issues.MayManage)
            },
            Invites = new InvitePermissions
            {
                MayCreate = inviters.Count > 0,
                MaxInvites = Largest(inviters.Select(x => x.Invites.MaxInvites)),
                AnyGroup = inviters.Any(x => x.Invites.AnyGroup),
                TargetGroupIds = inviters.SelectMany(x => x.Invites.TargetGroupIds).Distinct().ToList()
            },
            WireGuard = new WireGuardPermissions
            {
                MayHavePeers = peerHolders.Count > 0,
                MaxPeers = Largest(peerHolders.Select(x => x.WireGuard.MaxPeers))
            },
            Scripts = new ScriptPermissions
            {
                AllScripts = p.Any(x => x.Scripts.AllScripts),
                ScriptIds = Union(p.SelectMany(x => x.Scripts.ScriptIds))
            },
            CustomPages = new CustomPagePermissions
            {
                AllPages = p.Any(x => x.CustomPages.AllPages),
                PageIds = Union(p.SelectMany(x => x.CustomPages.PageIds))
            },
            Notifications = new NotificationPermissions
            {
                AllEvents = p.Any(x => x.Notifications.AllEvents),
                EventKeys = Union(p.SelectMany(x => x.Notifications.EventKeys))
            }
        };
    }

    /// <summary>
    /// Builds what an administrator may do, which is everything, with no limit and no account rule.
    /// </summary>
    /// <param name="userId">The administrator.</param>
    /// <returns>The administrator's permissions.</returns>
    public static EffectivePermissions Administrator(Guid userId) => new()
    {
        UserId = userId,
        IsAdministrator = true,
        Requests = new RequestPermissions { MayRequest = true, AutoApprove = true, MaySeeOthers = true, MayManage = true },
        Issues = new IssuePermissions { MayReport = true, MayManage = true },
        Invites = new InvitePermissions { MayCreate = true, MaxInvites = 0, AnyGroup = true },
        WireGuard = new WireGuardPermissions { MayHavePeers = true, MaxPeers = 0 },
        Scripts = new ScriptPermissions { AllScripts = true },
        CustomPages = new CustomPagePermissions { AllPages = true },
        Notifications = new NotificationPermissions { AllEvents = true }
    };

    /// <summary>
    /// Builds the permissions of a user the server does not have, which allow nothing.
    /// </summary>
    /// <param name="userId">The id that matched no user.</param>
    /// <returns>Permissions that allow nothing.</returns>
    public static EffectivePermissions Nobody(Guid userId) => new()
    {
        UserId = userId,
        Exists = false,
        Invites = new InvitePermissions { MaxInvites = 0 },
        WireGuard = new WireGuardPermissions { MaxPeers = 0 }
    };

    /// <summary>
    /// Takes the largest of several limits, where 0 is no limit and so beats any number.
    /// </summary>
    /// <param name="limits">The limits the allowing groups give.</param>
    /// <returns>The largest limit, or 0 when there is none, which only counts while a group allows the action.</returns>
    public static int Largest(IEnumerable<int> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var list = limits.Select(l => Math.Max(0, l)).ToList();
        return list.Count == 0 || list.Contains(0) ? 0 : list.Max();
    }

    private static List<string> Union(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).ToList();
}
