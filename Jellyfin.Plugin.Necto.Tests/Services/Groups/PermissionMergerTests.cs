using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// A user's permissions come from their one group: a limit counts only while the group allows the action, with 0 as no
/// limit, lists hold each entry once, Everyone applies only to a user in no group, and administrators may do everything.
/// </summary>
public class PermissionMergerTests
{
    private static readonly Guid User = Guid.NewGuid();

    /// <summary>
    /// What the group allows is allowed, and nothing else.
    /// True: a member of Family may request and manage issues when Family allows them.
    /// False: a member would get a permission the group does not give, or lose one it does.
    /// </summary>
    [Fact]
    public void Merge_TheGroupsAllows_Allow()
    {
        var family = TestGroups.Make("Family", p => { p.Requests.MayRequest = true; p.Issues.MayManage = true; }, User);

        var merged = PermissionMerger.Merge(User, family);

        Assert.Equal(new[] { family.Id }, merged.GroupIds);
        Assert.True(merged.Requests.MayRequest);
        Assert.True(merged.Issues.MayManage);
        Assert.False(merged.Issues.MayReport);
        Assert.False(merged.WireGuard.MayHavePeers);
    }

    /// <summary>
    /// A limit counts only while the group allows the action, and 0 is no limit.
    /// True: a group with 5 peers gives 5, and one with no movie quota gives no limit.
    /// False: a limit of a section that is off would read as no limit, or 0 as the smallest limit.
    /// </summary>
    [Fact]
    public void Merge_Limits_CountOnlyWhileAllowed()
    {
        var a = TestGroups.Make("A", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.MaxPeers = 5; p.Requests.MayRequest = true; p.Requests.MovieQuota = 0; p.Requests.SeriesQuota = 4; p.Issues.Limit = 0; p.Invites.MaxInvites = 0; }, User);

        var merged = PermissionMerger.Merge(User, a);

        Assert.Equal(5, merged.WireGuard.MaxPeers);
        Assert.Equal(0, merged.Requests.MovieQuota);
        Assert.Equal(4, merged.Requests.SeriesQuota);
        Assert.False(merged.Issues.MayReport);
        Assert.Equal(0, merged.Issues.Limit);
        Assert.False(merged.Invites.MayCreate);
        Assert.Equal(0, merged.Invites.MaxInvites);
    }

    /// <summary>
    /// Lists hold the group's entries once each.
    /// True: a member opens the group's pages and may invite to its groups.
    /// False: a page the group gives would be missing, or one it does not give would open.
    /// </summary>
    [Fact]
    public void Merge_Lists_HoldTheGroupsEntries()
    {
        var target = Guid.NewGuid();
        var a = TestGroups.Make("A", p =>
        {
            p.CustomPages.PageIds.Add("news"); p.CustomPages.PageIds.Add("rules"); p.CustomPages.PageIds.Add("news");
            p.Invites.MayCreate = true; p.Invites.TargetGroupIds.Add(target);
            p.Notifications.ByMethod = true; p.Notifications.BrowserEventKeys = new() { "requests.created" };
        }, User);

        var merged = PermissionMerger.Merge(User, a);

        Assert.Equal(new[] { "news", "rules" }, merged.CustomPages.PageIds);
        Assert.True(merged.MayOpenPage("rules"));
        Assert.False(merged.MayOpenPage("other"));
        Assert.True(merged.GetsInBrowsers("requests.created"));
        Assert.False(merged.GetsAsMessage("requests.created"));
        Assert.True(merged.MayInviteTo(target));
        Assert.False(merged.MayInviteTo(Guid.NewGuid()));
    }

    /// <summary>
    /// A stored group's events read as its methods allowed them, and only events about users count.
    /// True: a group stored with browsers off keeps its events as messages only after the upgrade.
    /// False: an upgrade would start or stop notifications members had.
    /// </summary>
    [Fact]
    public void Merge_StoredNotifications_ReadByMethod()
    {
        var quiet = TestGroups.Make("Quiet", p => { p.Notifications.EventKeys.Add("requests.approved"); p.Notifications.EventKeys.Add("proxy.banned"); p.Notifications.BrowserNotifications = false; }, User);
        var loud = TestGroups.Make("Loud", p => p.Notifications.AllEvents = true, User);

        var merged = PermissionMerger.Merge(User, quiet);
        var all = PermissionMerger.Merge(User, loud);

        Assert.False(merged.GetsInBrowsers("requests.approved"));
        Assert.True(merged.GetsAsMessage("requests.approved"));
        Assert.True(all.GetsInBrowsers("issues.updated"));
        Assert.True(PermissionMerger.Administrator(User).GetsInBrowsers("proxy.banned"));

        Jellyfin.Plugin.Necto.Services.Groups.GroupNormalizer.Complete(quiet);
        Assert.Empty(quiet.Permissions.Notifications.BrowserEventKeys!);
        Assert.Equal(new[] { "requests.approved" }, quiet.Permissions.Notifications.MessageEventKeys);
        Assert.Empty(quiet.Permissions.Notifications.EventKeys);
    }

    /// <summary>
    /// Everyone applies to a user in no group.
    /// True: an ungrouped user gets what Everyone gives.
    /// False: users outside every group would get nothing, or something else.
    /// </summary>
    [Fact]
    public void Resolve_UserInNoGroup_TakesEveryone()
    {
        var config = TestGroups.Config(TestGroups.Make("Family", p => p.Issues.MayManage = true, Guid.NewGuid()));
        config.Everyone.Permissions.Issues.MayReport = true;

        var merged = PermissionService.Resolve(config, User, isAdministrator: false);

        Assert.Equal(new[] { Group.EveryoneId }, merged.GroupIds);
        Assert.True(merged.Issues.MayReport);
        Assert.False(merged.Issues.MayManage);
    }

    /// <summary>
    /// Everyone does not add to a real group.
    /// True: a member of Family gets only what Family gives, even when Everyone gives more.
    /// False: Everyone's allows would reach every member of every group.
    /// </summary>
    [Fact]
    public void Resolve_UserInAGroup_DoesNotTakeEveryone()
    {
        var config = TestGroups.Config(TestGroups.Make("Family", User));
        config.Everyone.Permissions.Issues.MayReport = true;

        var merged = PermissionService.Resolve(config, User, isAdministrator: false);

        Assert.DoesNotContain(Group.EveryoneId, merged.GroupIds);
        Assert.False(merged.Issues.MayReport);
    }

    /// <summary>
    /// An administrator may do everything, with no limit and no account rule.
    /// True: tools never refuse an administrator.
    /// False: an administrator in no group would be held to Everyone.
    /// </summary>
    [Fact]
    public void Resolve_Administrator_IsAllowedEverything()
    {
        var config = TestGroups.Config();
        config.Everyone.Permissions.Accounts.Password.Enabled = true;

        var merged = PermissionService.Resolve(config, User, isAdministrator: true);

        Assert.True(merged.IsAdministrator);
        Assert.True(merged.Requests.MayRequest && merged.Requests.AutoApproveMovies && merged.Requests.AutoApproveSeries && merged.Requests.MayManage && merged.Requests.MaySeeOthers);
        Assert.True(merged.Issues.MayReport && merged.Issues.MayManage);
        Assert.True(merged.WireGuard.MayHavePeers);
        Assert.Equal(0, merged.WireGuard.MaxPeers);
        Assert.True(merged.MayOpenPage("any") && merged.GetsInBrowsers("any") && merged.MayInviteTo(Guid.NewGuid()));
        Assert.Null(merged.Accounts.Password);
    }

    /// <summary>
    /// A limit of 0 or less is no limit.
    /// True: the rule is the same for every count a tool reads.
    /// False: a tool would read a different limit than the Groups page shows.
    /// </summary>
    [Theory]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    [InlineData(-2, 0)]
    public void Limit_TreatsZeroAndLessAsNoLimit(int limit, int expected)
    {
        Assert.Equal(expected, PermissionMerger.Limit(limit));
    }

    /// <summary>
    /// A group that allows requesting gives its types, limits, and profiles, each profile once.
    /// True: a member of an HD group requests movies in HD, up to its limits.
    /// False: a setting the group makes would be lost.
    /// </summary>
    [Fact]
    public void Requests_Requester_TakesTheGroupsValues()
    {
        var hd = Requests(r => { r.Series = false; r.AutoApproveMovies = true; r.MaxOpenRequests = 2; r.MovieQuota = 3; r.MaxSeasons = 2; r.MovieProfiles = new() { "hd", "4k", "hd" }; r.MaySeeOthers = true; });

        var merged = PermissionMerger.Requests(hd.Requests);

        Assert.True(merged.MayRequest);
        Assert.True(merged.Movies);
        Assert.False(merged.Series);
        Assert.True(merged.AutoApproveMovies);
        Assert.False(merged.AutoApproveSeries);
        Assert.Equal(2, merged.MaxOpenRequests);
        Assert.Equal(3, merged.MovieQuota);
        Assert.Equal(2, merged.MaxSeasons);
        Assert.Equal(new[] { "hd", "4k" }, merged.MovieProfiles);
        Assert.Empty(merged.SeriesProfiles);
        Assert.True(merged.MaySeeOthers);
        Assert.False(merged.MaySeeRequesters);
    }

    /// <summary>
    /// A group that does not allow requesting allows nothing in Requests but managing.
    /// True: a moderator group that does not request still manages requests.
    /// False: managing would need a request of one's own, or a group without requests would allow them.
    /// </summary>
    [Fact]
    public void Requests_NoRequester_AllowsOnlyManaging()
    {
        var moderators = new GroupPermissions();
        moderators.Requests.MayManage = true;
        moderators.Requests.MaySeeOthers = true;

        var merged = PermissionMerger.Requests(moderators.Requests);

        Assert.False(merged.MayRequest);
        Assert.False(merged.Movies);
        Assert.False(merged.MaySeeOthers);
        Assert.Equal(0, merged.MaxOpenRequests);
        Assert.True(merged.MayManage);
    }

    /// <summary>
    /// Issues give the limit only while the group allows reporting, and managing needs no report.
    /// True: a reporting group's limit counts, and a managing group manages without reporting.
    /// False: a non reporting group's limit would count, or managing would need reporting.
    /// </summary>
    [Fact]
    public void Merge_Issues_LimitWhileReportingAndManagingAlone()
    {
        var many = PermissionMerger.Merge(User, TestGroups.Make("M", p => { p.Issues.MayReport = true; p.Issues.Limit = 7; }));
        var managers = PermissionMerger.Merge(User, TestGroups.Make("Mods", p => { p.Issues.Limit = 50; p.Issues.MayManage = true; }));

        Assert.True(many.Issues.MayReport);
        Assert.Equal(7, many.Issues.Limit);
        Assert.False(many.Issues.MayManage);
        Assert.False(managers.Issues.MayReport);
        Assert.Equal(0, managers.Issues.Limit);
        Assert.True(managers.Issues.MayManage);
    }

    private static GroupPermissions Requests(Action<RequestPermissions> change)
    {
        var permissions = new GroupPermissions();
        permissions.Requests.MayRequest = true;
        change(permissions.Requests);
        return permissions;
    }
}
