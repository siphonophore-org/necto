using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Accounts;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// Normalizing keeps Everyone and every section, lists each member once, drops administrators and deleted users, and
/// clears settings that name a group that is gone.
/// </summary>
public class GroupNormalizerTests
{
    /// <summary>
    /// Everyone comes back when a save leaves it out, with no members and its own name.
    /// True: Everyone always exists and only stands in for ungrouped users.
    /// False: a post without Everyone would leave ungrouped users with nothing to resolve.
    /// </summary>
    [Fact]
    public void Normalize_MissingEveryone_IsRestored()
    {
        var config = new PluginConfiguration { Everyone = null! };

        Assert.True(GroupNormalizer.Normalize(config));

        Assert.Equal(Group.EveryoneId, config.Everyone.Id);
        Assert.Equal("Everyone", config.Everyone.Name);
        Assert.NotNull(config.Everyone.Permissions.Accounts.Sessions);
    }

    /// <summary>
    /// A stored setting that listed every reset code turns Show reset codes on for every group, Everyone too, and clears.
    /// True: a server that showed reset codes keeps showing them for every member after the setting moved to groups.
    /// False: the codes would vanish, or the old setting would keep overriding the groups.
    /// </summary>
    [Fact]
    public void Normalize_StoredShowResetCodes_MovesOntoEveryGroup()
    {
        var config = new PluginConfiguration();
        config.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "Kids" });
        config.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "Adults" });
        config.Groups[1].Permissions.Accounts.Resets = null!;
        config.AccountSettings.ShowResetCodes = true;

        Assert.True(GroupNormalizer.Normalize(config));

        Assert.False(config.AccountSettings.ShowResetCodes);
        Assert.All(config.Groups.Append(config.Everyone), g => Assert.True(g.Permissions.Accounts.Resets.ShowCodes));

        config.Groups[0].Permissions.Accounts.Resets.ShowCodes = false;
        GroupNormalizer.Normalize(config);
        Assert.False(config.Groups[0].Permissions.Accounts.Resets.ShowCodes);
    }

    /// <summary>
    /// Everyone holds no members, and a copy of it among the groups is dropped.
    /// True: Everyone is never a group a user is listed in.
    /// False: Everyone's permissions would add to a real group's.
    /// </summary>
    [Fact]
    public void Normalize_EveryoneMembersAndCopies_AreDropped()
    {
        var config = new PluginConfiguration();
        config.Everyone.MemberIds.Add(Guid.NewGuid());
        config.Everyone.Name = "Renamed";
        config.Groups.Add(new Group { Id = Group.EveryoneId, Name = "Copy" });

        GroupNormalizer.Normalize(config);

        Assert.Empty(config.Everyone.MemberIds);
        Assert.Equal("Everyone", config.Everyone.Name);
        Assert.Empty(config.Groups);
    }

    /// <summary>
    /// A member listed twice in one group is kept once, and a group without an id gets one.
    /// True: counts and merges see each member once.
    /// False: a member would count twice, and a group with no id could not be opened.
    /// </summary>
    [Fact]
    public void Normalize_DuplicatesAndMissingIds_AreFixed()
    {
        var user = Guid.NewGuid();
        var config = new PluginConfiguration { Groups = new List<Group> { new() { Name = " Family ", MemberIds = new List<Guid> { user, user, Guid.Empty } } } };

        GroupNormalizer.Normalize(config);

        Assert.NotEqual(Guid.Empty, config.Groups[0].Id);
        Assert.Equal("Family", config.Groups[0].Name);
        Assert.Equal(new[] { user }, config.Groups[0].MemberIds);
    }

    /// <summary>
    /// Administrators and deleted users hold no membership, and a deleted user's provider record goes.
    /// True: a promoted member stops being listed, and a deleted account stops counting.
    /// False: a deleted account would count as a member forever.
    /// </summary>
    [Fact]
    public void Normalize_AdministratorsAndDeletedUsers_AreDropped()
    {
        var member = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var config = TestGroups.Config(TestGroups.Make("Family", member, admin, gone));
        config.ProviderEnrollments.Add(new ProviderEnrollment { UserId = gone });
        config.ProviderEnrollments.Add(new ProviderEnrollment { UserId = admin });

        GroupNormalizer.Normalize(config, new[] { member, admin }, new[] { admin });

        Assert.Equal(new[] { member }, config.Groups[0].MemberIds);
        var kept = Assert.Single(config.ProviderEnrollments);
        Assert.Equal(admin, kept.UserId);
    }

    /// <summary>
    /// An empty user list counts as unreadable and leaves membership alone.
    /// True: a pass while the user list cannot be read never wipes the groups.
    /// False: a failed read would empty every group.
    /// </summary>
    [Fact]
    public void Normalize_EmptyUserList_LeavesMembershipAlone()
    {
        var member = Guid.NewGuid();
        var config = TestGroups.Config(TestGroups.Make("Family", member));

        GroupNormalizer.Normalize(config, Array.Empty<Guid>());

        Assert.Equal(new[] { member }, config.Groups[0].MemberIds);
    }

    /// <summary>
    /// Settings that name a group that is gone are cleared.
    /// True: a new user never joins a deleted group, and an invite never targets one.
    /// False: a deleted group's id would linger in the settings.
    /// </summary>
    [Fact]
    public void Normalize_SettingsNamingAMissingGroup_AreCleared()
    {
        var family = TestGroups.Make("Family");
        family.Permissions.Invites.TargetGroupIds.AddRange(new[] { family.Id, Guid.NewGuid() });
        var config = TestGroups.Config(family);
        config.GroupSettings.NewUserGroupId = Guid.NewGuid();

        GroupNormalizer.Normalize(config);

        Assert.Null(config.GroupSettings.NewUserGroupId);
        Assert.Equal(new[] { family.Id }, config.Groups[0].Permissions.Invites.TargetGroupIds);
    }

    /// <summary>
    /// A group posted with missing sections gets every section.
    /// True: every tool can read its section without a null check.
    /// False: a partial post would break the permission check.
    /// </summary>
    [Fact]
    public void Normalize_MissingSections_AreFilled()
    {
        var config = TestGroups.Config(new Group { Id = Guid.NewGuid(), Name = "Kids", Permissions = new GroupPermissions { Accounts = null!, Requests = null!, Scripts = null! } });

        GroupNormalizer.Normalize(config);

        Assert.NotNull(config.Groups[0].Permissions.Accounts.Password);
        Assert.NotNull(config.Groups[0].Permissions.Requests);
        Assert.NotNull(config.Groups[0].Permissions.Scripts.ScriptIds);
    }

    /// <summary>
    /// A settled configuration reports no change.
    /// True: a pass over saved groups asks for no save.
    /// False: every pass would write the settings file again.
    /// </summary>
    [Fact]
    public void Normalize_SettledConfiguration_ReportsNoChange()
    {
        var config = TestGroups.Config(TestGroups.Make("Family", Guid.NewGuid()));
        GroupNormalizer.Normalize(config);

        Assert.False(GroupNormalizer.Normalize(config));
    }
}
