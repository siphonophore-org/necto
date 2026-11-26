using System;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

/// <summary>
/// A script runs for a user when one of the user's groups runs it, for an administrator always, and for an unknown name
/// when Everyone runs it.
/// </summary>
public class ScriptGroupsTests
{
    /// <summary>
    /// A member's groups decide, by every script or by the script's id with or without dashes.
    /// True: assigning a script to a group runs it for that group's members.
    /// False: a script runs for members of no group that lists it.
    /// </summary>
    [Fact]
    public void RunsFor_FollowsTheUsersGroups()
    {
        var users = new FakeUsers();
        var member = users.Add("viewer");
        var outsider = users.Add("guest");
        var admin = users.Add("admin", admin: true);
        var script = Guid.NewGuid();
        var config = TestGroups.Config(TestGroups.Make("Family", p => p.Scripts.ScriptIds.Add(script.ToString("N")), member.Id));
        var store = new FakeConfigurationStore(config);
        var groups = new ScriptGroups(new PermissionService(store, users.Manager), store);

        Assert.True(groups.RunsFor(member.Id, script));
        Assert.False(groups.RunsFor(outsider.Id, script));
        Assert.False(groups.RunsFor(member.Id, Guid.NewGuid()));
        Assert.True(groups.RunsFor(admin.Id, Guid.NewGuid()));
    }

    /// <summary>
    /// An event about a name with no user takes Everyone.
    /// True: Everyone decides for failed sign ins with unknown names.
    /// False: such an event runs every script, or none.
    /// </summary>
    [Fact]
    public void RunsFor_UnknownUser_TakesEveryone()
    {
        var users = new FakeUsers();
        var store = new FakeConfigurationStore();
        var groups = new ScriptGroups(new PermissionService(store, users.Manager), store);
        var script = Guid.NewGuid();

        Assert.False(groups.RunsFor(Guid.Empty, script));
        store.Configuration.Everyone.Permissions.Scripts.ScriptIds.Add(script.ToString());
        Assert.True(groups.RunsFor(Guid.Empty, script));
    }

    /// <summary>
    /// Every script runs when a section says so.
    /// True: Run every script covers new scripts too.
    /// False: a new script is left out until it is listed.
    /// </summary>
    [Fact]
    public void Runs_AllScripts_RunsAnyScript()
        => Assert.True(ScriptGroups.Runs(new ScriptPermissions { AllScripts = true }, Guid.NewGuid()));
}
