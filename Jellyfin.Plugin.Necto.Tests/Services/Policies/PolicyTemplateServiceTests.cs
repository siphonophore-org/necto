using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DisplayPreferences = Jellyfin.Database.Implementations.Entities.DisplayPreferences;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Policies;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Policies;

/// <summary>
/// A user takes the template of the first of their groups, in group order, that has one on, and the template is
/// written onto their Jellyfin policy, settings, and home sections, never onto an administrator.
/// </summary>
public class PolicyTemplateServiceTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();

    private static Group WithTemplate(string name, Action<Jellyfin.Plugin.Necto.Configuration.Policies.PolicyTemplate> set, params Guid[] members)
    {
        var group = TestGroups.Make(name, members);
        group.Template.Enabled = true;
        set(group.Template);
        return group;
    }

    private PolicyTemplateService Service(IDisplayPreferencesManager? prefs = null) => new(_config, _users.Manager, NullLogger<PolicyTemplateService>.Instance, prefs);

    /// <summary>
    /// The first group in the list with a template on wins, and a group with it off is passed over.
    /// True: the group order decides whose template a member of several groups takes.
    /// False: two templates would fight over one policy.
    /// </summary>
    [Fact]
    public void GroupFor_TakesTheFirstGroupWithATemplateOn()
    {
        var user = Guid.NewGuid();
        var off = TestGroups.Make("Off", user);
        off.Template.Access.ManageIsHidden = true;
        var first = WithTemplate("First", _ => { }, user);
        var second = WithTemplate("Second", _ => { }, user);
        _config.Configuration.Groups.AddRange(new[] { off, first, second });

        Assert.Equal(first.Id, PolicyTemplateMerger.GroupFor(_config.Configuration, user)!.Id);

        GroupOrder.Apply(_config.Configuration.Groups, new[] { second.Id, off.Id, first.Id });
        Assert.Equal(second.Id, PolicyTemplateMerger.GroupFor(_config.Configuration, user)!.Id);
    }

    /// <summary>
    /// A user in no group takes Everyone's template, and none when Everyone has none.
    /// True: Everyone's template covers every loose user.
    /// False: users in no group would never take a template.
    /// </summary>
    [Fact]
    public void GroupFor_UserInNoGroup_TakesEveryones()
    {
        var user = Guid.NewGuid();
        Assert.Null(PolicyTemplateMerger.GroupFor(_config.Configuration, user));

        _config.Configuration.Everyone.Template.Enabled = true;
        Assert.True(PolicyTemplateMerger.GroupFor(_config.Configuration, user)!.IsEveryone);
    }

    /// <summary>
    /// Reordering needs every group once.
    /// True: a bad post leaves the order as it was.
    /// False: a post could drop or double a group.
    /// </summary>
    [Fact]
    public void GroupOrder_NeedsEveryGroupOnce()
    {
        var a = TestGroups.Make("A");
        var b = TestGroups.Make("B");
        var groups = new List<Group> { a, b };

        Assert.False(GroupOrder.Apply(groups, new[] { a.Id }));
        Assert.False(GroupOrder.Apply(groups, new[] { a.Id, a.Id }));
        Assert.False(GroupOrder.Apply(groups, new[] { a.Id, Guid.NewGuid() }));
        Assert.True(GroupOrder.Apply(groups, new[] { b.Id, a.Id }));
        Assert.Equal(new[] { b.Id, a.Id }, groups.Select(g => g.Id));
    }

    /// <summary>
    /// A template writes its managed policy and settings onto a member.
    /// True: a member gets the group's access, features, and preferences.
    /// False: the template would change nothing.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_WritesThePolicyAndSettings()
    {
        var viewer = _users.Add("viewer");
        _users.Policies[viewer.Id].EnableRemoteAccess = true;
        _config.Configuration.Groups.Add(WithTemplate("Kids", t =>
        {
            t.Access.ManageEnableRemoteAccess = true;
            t.Access.EnableRemoteAccess = false;
            t.Preferences.ManageSubtitleMode = true;
            t.Preferences.SubtitleMode = "Always";
        }, viewer.Id));

        Assert.True(await Service().ApplyAsync(viewer));

        Assert.False(_users.Policies[viewer.Id].EnableRemoteAccess);
        Assert.Equal(SubtitlePlaybackMode.Always, _users.Configurations[viewer.Id].SubtitleMode);
    }

    /// <summary>
    /// A template that manages no preference leaves the settings alone.
    /// True: a policy only template never rewrites a member's settings.
    /// False: every apply would rewrite every member's settings.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ManagesNoPreference_LeavesSettings()
    {
        var viewer = _users.Add("viewer");
        _config.Configuration.Groups.Add(WithTemplate("Kids", t => t.Access.ManageIsHidden = true, viewer.Id));

        await Service().ApplyAsync(viewer);

        Assert.False(_users.Configurations.ContainsKey(viewer.Id));
    }

    /// <summary>
    /// An administrator is never changed, and a user with no template is left alone.
    /// True: templates never touch an administrator's policy.
    /// False: an administrator could lose access through a group.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_AdministratorOrNoTemplate_IsLeftAlone()
    {
        var admin = _users.Add("admin", admin: true);
        var loose = _users.Add("loose");
        _config.Configuration.Everyone.Template.Enabled = false;

        Assert.False(await Service().ApplyAsync(admin));
        Assert.False(await Service().ApplyAsync(loose));
    }

    /// <summary>
    /// A template that turns accounts on leaves off one its groups expired.
    /// True: expiry keeps a member disabled even under a template that enables members.
    /// False: the next apply would turn an expired account back on.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_TemplateEnables_ButExpiryHoldsTheAccount()
    {
        var viewer = _users.Add("viewer");
        _users.Policies[viewer.Id].IsDisabled = true;
        var group = WithTemplate("Trial", t => { t.Access.ManageIsDisabled = true; t.Access.IsDisabled = false; }, viewer.Id);
        group.Permissions.Accounts.Expiry.ExpiresOn = DateTime.UtcNow.Date.AddDays(-1);
        _config.Configuration.Groups.Add(group);

        await Service().ApplyAsync(viewer);

        Assert.True(_users.Policies[viewer.Id].IsDisabled);
    }

    /// <summary>
    /// Inactivity past the window holds an account, and a recent one does not.
    /// True: the template and the inactivity rule agree.
    /// False: a template would undo the inactivity rule.
    /// </summary>
    [Theory]
    [InlineData(40, true)]
    [InlineData(5, false)]
    public void HeldByAccountRules_FollowsInactivity(int daysAgo, bool held)
    {
        var now = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
        var dto = new UserDto { LastActivityDate = now.AddDays(-daysAgo) };

        Assert.Equal(held, PolicyTemplateService.HeldByAccountRules(dto, new EffectiveAccounts { InactiveDays = 30 }, now));
    }

    /// <summary>
    /// A managed home layout is written to the web client's display preferences in order, and an unmanaged one is not.
    /// True: a group sets its members' home screen sections.
    /// False: the sections would be lost or written for every group.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_HomeSections_AreWrittenInOrderOnlyWhenManaged()
    {
        var viewer = _users.Add("viewer");
        var (prefs, stub) = InterfaceStub.Create<IDisplayPreferencesManager>();
        var entity = new DisplayPreferences(viewer.Id, Guid.Empty, "emby");
        stub.Answer("GetDisplayPreferences", _ => entity);
        var group = WithTemplate("Kids", t => { t.Preferences.ManageHomeSections = true; t.Preferences.HomeSections = new List<string> { "resume", "nextup", "none" }; }, viewer.Id);
        _config.Configuration.Groups.Add(group);

        await Service(prefs).ApplyAsync(viewer);

        Assert.Equal(new[] { HomeSectionType.Resume, HomeSectionType.NextUp, HomeSectionType.None }, entity.HomeSections.OrderBy(h => h.Order).Select(h => h.Type));
        Assert.Contains(stub.Calls, c => c.Name == "UpdateDisplayPreferences");

        stub.Calls.Clear();
        group.Template.Preferences.ManageHomeSections = false;
        await Service(prefs).ApplyAsync(viewer);
        Assert.DoesNotContain(stub.Calls, c => c.Name == "UpdateDisplayPreferences");
    }

    /// <summary>
    /// Applying to everyone counts the users who took a template.
    /// True: Troubleshooting reports how many users changed.
    /// False: the count would be wrong.
    /// </summary>
    [Fact]
    public async Task ApplyAllAsync_CountsUsersWithATemplate()
    {
        var a = _users.Add("a");
        _users.Add("b");
        _users.Add("admin", admin: true);
        _config.Configuration.Groups.Add(WithTemplate("Kids", t => t.Access.ManageIsHidden = true, a.Id));

        Assert.Equal(1, await Service().ApplyAllAsync(null, default));
    }
}
