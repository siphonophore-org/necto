using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// The permission rule holds: an allow from any group allows, a limit takes the largest any allowing group gives with
/// 0 as no limit, lists hold what any group lists, Everyone applies only to a user in no group, and administrators may
/// do everything.
/// </summary>
public class PermissionMergerTests
{
    private static readonly Guid User = Guid.NewGuid();

    /// <summary>
    /// One group's allow is enough, whatever the other groups say.
    /// True: a member of Family and Kids may request when only Family allows it.
    /// False: an extra group with less would take a permission away.
    /// </summary>
    [Fact]
    public void Merge_AnAllowFromAnyGroup_Allows()
    {
        var family = TestGroups.Make("Family", p => { p.Requests.MayRequest = true; p.Issues.MayManage = true; }, User);
        var kids = TestGroups.Make("Kids", User);

        var merged = PermissionMerger.Merge(User, new[] { family, kids });

        Assert.True(merged.Requests.MayRequest);
        Assert.True(merged.Issues.MayManage);
        Assert.False(merged.Issues.MayReport);
        Assert.False(merged.WireGuard.MayHavePeers);
    }

    /// <summary>
    /// A limit takes the largest any group gives.
    /// True: a member with 2 and 5 peers allowed gets 5.
    /// False: a member in a second group would lose peers they were given.
    /// </summary>
    [Fact]
    public void Merge_Limits_TakeTheLargest()
    {
        var a = TestGroups.Make("A", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.MaxPeers = 2; }, User);
        var b = TestGroups.Make("B", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.MaxPeers = 5; }, User);

        var merged = PermissionMerger.Merge(User, new[] { a, b });

        Assert.Equal(5, merged.WireGuard.MaxPeers);
    }

    /// <summary>
    /// 0 is no limit, so it beats any number.
    /// True: a group with no movie quota lifts the quota of a member also in a group with 3.
    /// False: 0 would read as the smallest limit and block every request.
    /// </summary>
    [Fact]
    public void Merge_NoLimit_BeatsAnyNumber()
    {
        var a = TestGroups.Make("A", p => { p.Requests.MayRequest = true; p.Requests.MovieQuota = 3; }, User);
        var b = TestGroups.Make("B", p => { p.Requests.MayRequest = true; p.Requests.MovieQuota = 0; }, User);

        var merged = PermissionMerger.Merge(User, new[] { a, b });

        Assert.Equal(0, merged.Requests.MovieQuota);
    }

    /// <summary>
    /// A limit counts only from groups that allow the action.
    /// True: a group that gives no requests cannot lift the quota of one that does.
    /// False: an unrelated group's default 0 would read as no limit.
    /// </summary>
    [Fact]
    public void Merge_LimitFromAGroupThatDoesNotAllow_IsIgnored()
    {
        var a = TestGroups.Make("A", p => { p.Requests.MayRequest = true; p.Requests.SeriesQuota = 4; }, User);
        var b = TestGroups.Make("B", p => { p.Requests.SeriesQuota = 0; }, User);

        var merged = PermissionMerger.Merge(User, new[] { a, b });

        Assert.Equal(4, merged.Requests.SeriesQuota);
    }

    /// <summary>
    /// Lists hold what any group lists, once each.
    /// True: a member of two groups opens both groups' pages.
    /// False: a page given by one group would be missing.
    /// </summary>
    [Fact]
    public void Merge_Lists_HoldEveryGroupsEntries()
    {
        var target = Guid.NewGuid();
        var a = TestGroups.Make("A", p => { p.CustomPages.PageIds.Add("news"); p.Invites.MayCreate = true; p.Invites.TargetGroupIds.Add(target); }, User);
        var b = TestGroups.Make("B", p => { p.CustomPages.PageIds.Add("news"); p.CustomPages.PageIds.Add("rules"); p.Notifications.EventKeys.Add("request.new"); }, User);

        var merged = PermissionMerger.Merge(User, new[] { a, b });

        Assert.Equal(new[] { "news", "rules" }, merged.CustomPages.PageIds);
        Assert.True(merged.MayOpenPage("rules"));
        Assert.False(merged.MayOpenPage("other"));
        Assert.True(merged.Receives("request.new"));
        Assert.True(merged.MayInviteTo(target));
        Assert.False(merged.MayInviteTo(Guid.NewGuid()));
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
        Assert.True(merged.Requests.MayRequest && merged.Requests.AutoApprove && merged.Requests.MayManage && merged.Requests.MaySeeOthers);
        Assert.True(merged.Issues.MayReport && merged.Issues.MayManage);
        Assert.True(merged.WireGuard.MayHavePeers);
        Assert.Equal(0, merged.WireGuard.MaxPeers);
        Assert.True(merged.MayOpenPage("any") && merged.RunsScript("any") && merged.Receives("any") && merged.MayInviteTo(Guid.NewGuid()));
        Assert.Null(merged.Accounts.Password);
    }

    /// <summary>
    /// The largest limit treats 0 as no limit, and gives 0 with no limit to take.
    /// True: the rule is the same for every count a tool reads.
    /// False: a tool would read a different limit than the Groups page shows.
    /// </summary>
    [Theory]
    [InlineData(new[] { 1, 3, 2 }, 3)]
    [InlineData(new[] { 4, 0 }, 0)]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { -2, 5 }, 0)]
    public void Largest_TakesTheLargestWithZeroAsNoLimit(int[] limits, int expected)
    {
        Assert.Equal(expected, PermissionMerger.Largest(limits));
    }
}
