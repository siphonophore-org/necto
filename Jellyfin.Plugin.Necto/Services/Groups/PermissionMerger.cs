using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Works out what a user may do from their one group. A count or limit counts only while the group allows the action,
/// where 0 is no limit, and a list holds each entry once.
/// </summary>
public static class PermissionMerger
{
    /// <summary>
    /// Works out what a user may do from the group that applies to them.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="group">The group that applies to the user, Everyone when the user is in no group.</param>
    /// <returns>The user's permissions.</returns>
    public static EffectivePermissions Merge(Guid userId, Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var p = group.Permissions ?? new GroupPermissions();
        var invites = p.Invites;
        var peers = p.WireGuard;
        var issues = p.Issues;
        var limited = peers.MayHavePeers && peers.DataLimit > 0;

        return new EffectivePermissions
        {
            UserId = userId,
            GroupIds = new List<Guid> { group.Id },
            Accounts = new EffectiveAccounts
            {
                Password = AccountRuleMerger.Password(group),
                ExpiresOn = AccountRuleMerger.ExpiresOn(group),
                ExpiryAction = AccountRuleMerger.ExpiryActionOf(group),
                InactiveDays = AccountRuleMerger.InactiveDays(group),
                CleansSessions = AccountRuleMerger.CleansSessions(group)
            },
            Requests = Requests(p.Requests),
            Issues = new IssuePermissions
            {
                MayReport = issues.MayReport,
                Limit = issues.MayReport ? Limit(issues.Limit) : 0,
                MayManage = issues.MayManage
            },
            Invites = new InvitePermissions
            {
                MayCreate = invites.MayCreate,
                MaxInvites = invites.MayCreate ? Limit(invites.MaxInvites) : 0,
                AnyGroup = invites.MayCreate && invites.AnyGroup,
                TargetGroupIds = invites.MayCreate ? invites.TargetGroupIds.Distinct().ToList() : new List<Guid>()
            },
            WireGuard = new WireGuardPermissions
            {
                MayHavePeers = peers.MayHavePeers,
                MaxPeers = peers.MayHavePeers ? Limit(peers.MaxPeers) : 0,
                DataLimit = limited ? peers.DataLimit : 0,
                DataPeriod = limited ? peers.DataPeriod : new WireGuardPermissions().DataPeriod
            },
            Scripts = new ScriptPermissions
            {
                AllScripts = p.Scripts.AllScripts,
                ScriptIds = Union(p.Scripts.ScriptIds)
            },
            CustomPages = new CustomPagePermissions
            {
                AllPages = p.CustomPages.AllPages,
                PageIds = Union(p.CustomPages.PageIds)
            },
            Notifications = new NotificationPermissions
            {
                ByMethod = true,
                BrowserEventKeys = Union(Notifications.NotificationDelivery.Browser(p.Notifications)),
                MessageEventKeys = Union(Notifications.NotificationDelivery.Messages(p.Notifications))
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
        Requests = new RequestPermissions
        {
            MayRequest = true, AutoApproveMovies = true, AutoApproveSeries = true, MaxOpenRequests = 0, MaySeeOthers = true, MaySeeRequesters = true, MayManage = true
        },
        Issues = new IssuePermissions { MayReport = true, Limit = 0, MayManage = true },
        Invites = new InvitePermissions { MayCreate = true, MaxInvites = 0, AnyGroup = true },
        WireGuard = new WireGuardPermissions { MayHavePeers = true, MaxPeers = 0 },
        Scripts = new ScriptPermissions { AllScripts = true },
        CustomPages = new CustomPagePermissions { AllPages = true },
        Notifications = new NotificationPermissions { AllEvents = true, ByMethod = true, BrowserEventKeys = new List<string>(), MessageEventKeys = new List<string>() }
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
        Requests = new RequestPermissions { Movies = false, Series = false, Unreleased = false, MaxOpenRequests = 0 },
        Issues = new IssuePermissions { Limit = 0 },
        Invites = new InvitePermissions { MaxInvites = 0 },
        WireGuard = new WireGuardPermissions { MaxPeers = 0 }
    };

    /// <summary>
    /// Reads a limit, where 0 or less is no limit.
    /// </summary>
    /// <param name="limit">The group's limit.</param>
    /// <returns>The limit, or 0 for no limit.</returns>
    public static int Limit(int limit) => Math.Max(0, limit);

    /// <summary>
    /// Works out what a group gives in Requests. Types, quotas, profiles, and seeing others' requests count only while
    /// the group allows requesting, and an empty profile list holds every profile. Managing requests needs no request
    /// of one's own.
    /// </summary>
    /// <param name="r">The group's Requests section.</param>
    /// <returns>The user's Requests section.</returns>
    public static RequestPermissions Requests(RequestPermissions r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var may = r.MayRequest;
        return new RequestPermissions
        {
            MayRequest = may,
            Movies = may && r.Movies,
            Series = may && r.Series,
            Unreleased = may && r.Unreleased,
            AutoApproveMovies = may && r.AutoApproveMovies,
            AutoApproveSeries = may && r.AutoApproveSeries,
            MaxOpenRequests = may ? Limit(r.MaxOpenRequests) : 0,
            MovieQuota = may ? Limit(r.MovieQuota) : 0,
            SeriesQuota = may ? Limit(r.SeriesQuota) : 0,
            MaxSeasons = may ? Limit(r.MaxSeasons) : 0,
            MovieProfiles = may ? Union(r.MovieProfiles ?? new List<string>()) : new List<string>(),
            SeriesProfiles = may ? Union(r.SeriesProfiles ?? new List<string>()) : new List<string>(),
            MaySeeOthers = may && r.MaySeeOthers,
            MaySeeRequesters = may && r.MaySeeRequesters,
            MayManage = r.MayManage
        };
    }

    private static List<string> Union(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).ToList();
}
