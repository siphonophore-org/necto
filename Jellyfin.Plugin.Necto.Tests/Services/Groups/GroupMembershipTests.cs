using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// A user's groups are every group that lists them, in list order, or only Everyone when none does.
/// </summary>
public class GroupMembershipTests
{
    /// <summary>
    /// A user may be in several groups.
    /// True: every group that lists the user applies.
    /// False: only the first group would count, as a single membership model would.
    /// </summary>
    [Fact]
    public void GroupsOf_UserInSeveralGroups_GetsEachInOrder()
    {
        var user = Guid.NewGuid();
        var a = TestGroups.Make("A", user);
        var b = TestGroups.Make("B");
        var c = TestGroups.Make("C", user);

        var groups = GroupMembership.GroupsOf(TestGroups.Config(a, b, c), user);

        Assert.Equal(new[] { a.Id, c.Id }, new[] { groups[0].Id, groups[1].Id });
        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// A user in no group takes Everyone.
    /// True: every user has exactly one source of permissions or more.
    /// False: an ungrouped user would have no group at all.
    /// </summary>
    [Fact]
    public void GroupsOf_UserInNoGroup_GetsEveryone()
    {
        var groups = GroupMembership.GroupIdsOf(TestGroups.Config(TestGroups.Make("A", Guid.NewGuid())), Guid.NewGuid());

        Assert.Equal(new[] { Group.EveryoneId }, groups);
    }
}
