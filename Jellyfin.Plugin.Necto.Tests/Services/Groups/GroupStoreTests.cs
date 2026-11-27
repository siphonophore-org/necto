using System;
using System.Linq;
using Jellyfin.Data;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// The group store creates, saves, and deletes groups with unique names, keeps Everyone, holds only real users who
/// are not administrators, and writes an activity entry when a group is created or deleted.
/// </summary>
public class GroupStoreTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();
    private readonly FakeActivity _activity = new();

    private GroupStore Store() => new(_config, _users.Manager, _activity.Logger, FakeServerConfig.Create(), NullLogger<GroupStore>.Instance);

    /// <summary>
    /// A new group gets a new id, its members, and an activity entry.
    /// True: the page can open the group it just created, and the activity log shows it.
    /// False: a posted id could overwrite another group, or a creation would go unrecorded.
    /// </summary>
    [Fact]
    public void Create_SavesTheGroupWithANewId()
    {
        var viewer = _users.Add("viewer");
        var posted = new Group { Id = Group.EveryoneId, Name = " Family ", MemberIds = { viewer.Id } };

        var change = Store().Create(posted);

        Assert.True(change.Succeeded);
        Assert.NotEqual(Group.EveryoneId, change.Group!.Id);
        Assert.Equal("Family", change.Group.Name);
        Assert.Equal(new[] { viewer.Id }, change.Group.MemberIds);
        Assert.Single(_config.Configuration.Groups);
        Assert.Contains("Group created", _activity.Names);
    }

    /// <summary>
    /// A group needs a name no other group has, Everyone's included, whatever the case.
    /// True: every group is told apart by name on the page and in the activity log.
    /// False: two groups could share a name, or one could be called Everyone.
    /// </summary>
    [Theory]
    [InlineData("", GroupChangeError.NameRequired)]
    [InlineData("  ", GroupChangeError.NameRequired)]
    [InlineData("family", GroupChangeError.NameTaken)]
    [InlineData("EVERYONE", GroupChangeError.NameTaken)]
    public void Create_BadName_IsRefused(string name, GroupChangeError expected)
    {
        Store().Create(new Group { Name = "Family" });

        var change = Store().Create(new Group { Name = name });

        Assert.Equal(expected, change.Error);
        Assert.Single(_config.Configuration.Groups);
    }

    /// <summary>
    /// An administrator or an unknown id posted as a member is dropped.
    /// True: no group lists an administrator, whom groups never hold.
    /// False: an administrator would be counted, and an invented id kept.
    /// </summary>
    [Fact]
    public void Create_AdministratorsAndUnknownIds_AreNotMembers()
    {
        var viewer = _users.Add("viewer");
        var admin = _users.Add("admin", admin: true);

        var change = Store().Create(new Group { Name = "Family", MemberIds = { viewer.Id, admin.Id, Guid.NewGuid() } });

        Assert.Equal(new[] { viewer.Id }, change.Group!.MemberIds);
    }

    /// <summary>
    /// Saving a group replaces its name, members, and permissions.
    /// True: the sidecar's Save keeps what it shows.
    /// False: a save would keep the old values.
    /// </summary>
    [Fact]
    public void Update_ReplacesTheGroup()
    {
        var viewer = _users.Add("viewer");
        var id = Store().Create(new Group { Name = "Family" }).Group!.Id;
        var next = new Group { Name = "Friends", MemberIds = { viewer.Id } };
        next.Permissions.Issues.MayReport = true;

        var change = Store().Update(id, next);

        Assert.True(change.Succeeded);
        var saved = Assert.Single(_config.Configuration.Groups);
        Assert.Equal(id, saved.Id);
        Assert.Equal("Friends", saved.Name);
        Assert.True(saved.Permissions.Issues.MayReport);
        Assert.Equal(new[] { viewer.Id }, saved.MemberIds);
    }

    /// <summary>
    /// Everyone saves its permissions and keeps its name and empty member list.
    /// True: Everyone can be given permissions, and stays the group for ungrouped users.
    /// False: Everyone could be renamed or given members.
    /// </summary>
    [Fact]
    public void Update_Everyone_KeepsItsNameAndHoldsNoMembers()
    {
        var viewer = _users.Add("viewer");
        var next = new Group { Name = "Renamed", MemberIds = { viewer.Id } };
        next.Permissions.Requests.MayRequest = true;

        var change = Store().Update(Group.EveryoneId, next);

        Assert.True(change.Succeeded);
        Assert.Equal("Everyone", _config.Configuration.Everyone.Name);
        Assert.Empty(_config.Configuration.Everyone.MemberIds);
        Assert.True(_config.Configuration.Everyone.Permissions.Requests.MayRequest);
    }

    /// <summary>
    /// Saving a group that does not exist is refused.
    /// True: the page says the group is gone instead of creating it.
    /// False: a stale page would bring a deleted group back.
    /// </summary>
    [Fact]
    public void Update_UnknownGroup_IsNotFound()
    {
        Assert.Equal(GroupChangeError.NotFound, Store().Update(Guid.NewGuid(), new Group { Name = "Family" }).Error);
    }

    /// <summary>
    /// Deleting a group removes it, writes an entry, and clears the settings that name it.
    /// True: its members fall back to their other groups or Everyone.
    /// False: a deleted group would keep applying, or linger as the group for new users.
    /// </summary>
    [Fact]
    public void Delete_RemovesTheGroup()
    {
        var id = Store().Create(new Group { Name = "Family" }).Group!.Id;
        _config.Configuration.GroupSettings.NewUserGroupId = id;

        var change = Store().Delete(id);

        Assert.True(change.Succeeded);
        Assert.Empty(_config.Configuration.Groups);
        Assert.Null(_config.Configuration.GroupSettings.NewUserGroupId);
        Assert.Contains("Group deleted", _activity.Names);
    }

    /// <summary>
    /// Everyone cannot be deleted, and an unknown group is not found.
    /// True: Everyone always exists.
    /// False: deleting Everyone would leave ungrouped users with nothing.
    /// </summary>
    [Fact]
    public void Delete_EveryoneOrUnknown_IsRefused()
    {
        Assert.Equal(GroupChangeError.EveryoneFixed, Store().Delete(Group.EveryoneId).Error);
        Assert.Equal(GroupChangeError.NotFound, Store().Delete(Guid.NewGuid()).Error);
    }

    /// <summary>
    /// The list starts with Everyone and holds copies.
    /// True: the page draws Everyone first, and a caller cannot change the stored groups by accident.
    /// False: a change to a listed group would reach the configuration without a save.
    /// </summary>
    [Fact]
    public void List_StartsWithEveryone_AndHoldsCopies()
    {
        Store().Create(new Group { Name = "Family" });

        var list = Store().List();

        Assert.Equal(new[] { "Everyone", "Family" }, list.Select(g => g.Name));
        Assert.True(list[0].IsEveryone);
        list[1].Name = "Changed";
        Assert.Equal("Family", _config.Configuration.Groups[0].Name);
    }

    /// <summary>
    /// Cleaning the membership drops a member promoted to administrator.
    /// True: a promotion takes the user out of every group on the next pass.
    /// False: an administrator would stay listed as a member.
    /// </summary>
    [Fact]
    public void CleanMembership_PromotedMember_IsDropped()
    {
        var viewer = _users.Add("viewer");
        Store().Create(new Group { Name = "Family", MemberIds = { viewer.Id } });
        _users.Users[viewer.Id].SetPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator, true);

        Assert.True(Store().CleanMembership());

        Assert.Empty(_config.Configuration.Groups[0].MemberIds);
    }

    /// <summary>
    /// Every group change that saves raises Changed once, and a refused one raises nothing.
    /// True: WireGuard applies a group's peers the moment the group is saved.
    /// False: a peer a group now allows or refuses keeps its old state until some other change.
    /// </summary>
    [Fact]
    public void Changes_RaiseChanged_OnlyWhenSaved()
    {
        var store = Store();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        var id = store.Create(new Group { Name = "Family" }).Group!.Id;
        store.Create(new Group { Name = "family" });
        store.Update(id, new Group { Name = "Friends" });
        store.Update(Guid.NewGuid(), new Group { Name = "Other" });
        store.Delete(id);
        store.Delete(id);

        Assert.Equal(3, raised);
    }
}
