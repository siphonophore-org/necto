using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// A user's group is the one that lists them, the first in list order should a stored config list them twice, or Everyone.
/// </summary>
public class GroupMembershipTests
{
    /// <summary>
    /// A user is in one group, so a stored config that lists them twice gives only the first.
    /// True: one group decides everything a user may do.
    /// False: a second group would still add to a user's permissions.
    /// </summary>
    [Fact]
    public void GroupsOf_UserListedTwice_GetsTheFirst()
    {
        var user = Guid.NewGuid();
        var a = TestGroups.Make("A", user);
        var b = TestGroups.Make("B");
        var c = TestGroups.Make("C", user);

        var groups = GroupMembership.GroupsOf(TestGroups.Config(a, b, c), user);

        Assert.Equal(a.Id, Assert.Single(groups).Id);
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
