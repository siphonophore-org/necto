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
            Requests = RequestMerge(requesters, p),
            Issues = IssueMerge(p.Where(x => x.Issues.MayReport).ToList(), p),
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
                MaxPeers = Largest(peerHolders.Select(x => x.WireGuard.MaxPeers)),
                DataLimit = LargestData(peerHolders.Select(x => x.WireGuard)).DataLimit,
                DataPeriod = LargestData(peerHolders.Select(x => x.WireGuard)).DataPeriod
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
                EventKeys = Union(p.SelectMany(x => x.Notifications.EventKeys)),

                // A channel counts only from a group that gives events, so a group with none cannot add one.
                BrowserNotifications = p.Any(x => x.Notifications.ReceivesAny() && x.Notifications.BrowserNotifications),
                JellyfinMessages = p.Any(x => x.Notifications.ReceivesAny() && x.Notifications.JellyfinMessages)
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
        Requests = new RequestPermissions { Movies = false, Series = false, Unreleased = false, MaxOpenRequests = 0 },
        Issues = new IssuePermissions { Limit = 0 },
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

    /// <summary>
    /// Takes the most generous data limit of several groups, with its period. 0 is no limit, so it beats any number.
    /// </summary>
    /// <param name="limits">The WireGuard sections of the groups that allow peers.</param>
    /// <returns>The section whose limit is largest, or one with no limit when there is none.</returns>
    public static WireGuardPermissions LargestData(IEnumerable<WireGuardPermissions> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var list = limits.ToList();
        return list.Count == 0 || list.Any(l => l.DataLimit <= 0)
            ? new WireGuardPermissions { DataLimit = 0 }
            : list.OrderByDescending(l => l.DataLimit).First();
    }

    /// <summary>
    /// Merges what the groups that allow requesting give. A type or unreleased titles count when any of them allows it,
    /// a profile list holds every profile when any of them lists none, and seeing others' requests needs a group
    /// that allows requesting. Managing requests needs no request of one's own.
    /// </summary>
    /// <param name="requesters">The sections of the groups that allow requesting.</param>
    /// <param name="all">Every group's sections.</param>
    /// <returns>The merged Requests section.</returns>
    public static RequestPermissions RequestMerge(IReadOnlyList<GroupPermissions> requesters, IReadOnlyList<GroupPermissions> all)
    {
        ArgumentNullException.ThrowIfNull(requesters);
        ArgumentNullException.ThrowIfNull(all);
        var r = requesters.Select(x => x.Requests).ToList();
        return new RequestPermissions
        {
            MayRequest = r.Count > 0,
            Movies = r.Any(x => x.Movies),
            Series = r.Any(x => x.Series),
            Unreleased = r.Any(x => x.Unreleased),
            AutoApproveMovies = r.Any(x => x.AutoApproveMovies),
            AutoApproveSeries = r.Any(x => x.AutoApproveSeries),
            MaxOpenRequests = Largest(r.Select(x => x.MaxOpenRequests)),
            MovieQuota = Largest(r.Select(x => x.MovieQuota)),
            SeriesQuota = Largest(r.Select(x => x.SeriesQuota)),
            MaxSeasons = Largest(r.Select(x => x.MaxSeasons)),
            MovieProfiles = Profiles(r.Select(x => x.MovieProfiles)),
            SeriesProfiles = Profiles(r.Select(x => x.SeriesProfiles)),
            MaySeeOthers = r.Any(x => x.MaySeeOthers),
            MaySeeRequesters = r.Any(x => x.MaySeeRequesters),
            MayManage = all.Any(x => x.Requests.MayManage)
        };
    }

    /// <summary>
    /// Merges what the groups give in Issues. The limit counts only from the groups that allow reporting.
    /// </summary>
    /// <param name="reporters">The sections of the groups that allow reporting.</param>
    /// <param name="all">Every group's sections, since managing issues needs no report.</param>
    /// <returns>The merged Issues section.</returns>
    public static IssuePermissions IssueMerge(IReadOnlyList<GroupPermissions> reporters, IReadOnlyList<GroupPermissions> all)
    {
        ArgumentNullException.ThrowIfNull(reporters);
        ArgumentNullException.ThrowIfNull(all);
        return new IssuePermissions
        {
            MayReport = reporters.Count > 0,
            Limit = Largest(reporters.Select(x => x.Issues.Limit)),
            MayManage = all.Any(x => x.Issues.MayManage)
        };
    }

    // Every profile when any group lists none, else each listed profile once.
    private static List<string> Profiles(IEnumerable<IReadOnlyList<string>?> lists)
    {
        var all = lists.Select(l => l ?? new List<string>()).ToList();
        return all.Count == 0 || all.Any(l => l.Count == 0) ? new List<string>() : Union(all.SelectMany(l => l));
    }

    private static List<string> Union(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).ToList();
}
