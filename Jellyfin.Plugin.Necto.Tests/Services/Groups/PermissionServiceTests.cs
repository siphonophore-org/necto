using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// The permission service finds the user, treats an administrator as allowed everything, and allows nothing for a
/// user the server does not have.
/// </summary>
public class PermissionServiceTests
{
    /// <summary>
    /// A member gets their group's permissions.
    /// True: a tool asking about a member reads their groups.
    /// False: a tool would answer from the wrong groups.
    /// </summary>
    [Fact]
    public void For_Member_GetsTheirGroups()
    {
        var users = new FakeUsers();
        var viewer = users.Add("viewer");
        var store = new FakeConfigurationStore(TestGroups.Config(TestGroups.Make("Family", p => p.Requests.MayRequest = true, viewer.Id)));

        var permissions = new PermissionService(store, users.Manager).For(viewer.Id);

        Assert.True(permissions.Exists);
        Assert.False(permissions.IsAdministrator);
        Assert.True(permissions.Requests.MayRequest);
    }

    /// <summary>
    /// An administrator is allowed everything even when a group lists them.
    /// True: no group can limit an administrator.
    /// False: an administrator added to a group by hand would lose permissions.
    /// </summary>
    [Fact]
    public void For_Administrator_IsAllowedEverything()
    {
        var users = new FakeUsers();
        var admin = users.Add("admin", admin: true);
        var store = new FakeConfigurationStore(TestGroups.Config(TestGroups.Make("Family", admin.Id)));

        var permissions = new PermissionService(store, users.Manager).For(admin.Id);

        Assert.True(permissions.IsAdministrator);
        Assert.True(permissions.Requests.MayManage);
    }

    /// <summary>
    /// A user the server does not have is allowed nothing, not Everyone.
    /// True: a deleted account's id grants no access.
    /// False: any id would take Everyone's permissions.
    /// </summary>
    [Fact]
    public void For_UnknownUser_AllowsNothing()
    {
        var store = new FakeConfigurationStore();
        store.Configuration.Everyone.Permissions.Issues.MayReport = true;

        var permissions = new PermissionService(store, new FakeUsers().Manager).For(Guid.NewGuid());

        Assert.False(permissions.Exists);
        Assert.False(permissions.Issues.MayReport);
        Assert.Empty(permissions.GroupIds);
        Assert.DoesNotContain(Group.EveryoneId, permissions.GroupIds);
    }
}
