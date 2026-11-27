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
/// A user takes the Jellyfin settings their one group sets, and they are
/// written onto their Jellyfin policy, settings, and home sections, never onto an administrator.
/// </summary>
public class PolicyTemplateServiceTests
{
    private readonly FakeUsers _users = new();
    private readonly FakeConfigurationStore _config = new();

    private static Group WithTemplate(string name, Action<Jellyfin.Plugin.Necto.Configuration.Policies.PolicyTemplate> set, params Guid[] members)
    {
        var group = TestGroups.Make(name, members);
        set(group.Template);
        return group;
    }

    private PolicyTemplateService Service(IDisplayPreferencesManager? prefs = null) => new(_config, _users.Manager, NullLogger<PolicyTemplateService>.Instance, prefs);

    /// <summary>
    /// A user takes the rows of their one group, and a stored second group that lists them adds nothing.
    /// True: one group decides a member's Jellyfin settings.
    /// False: a second group would still change a member.
    /// </summary>
    [Fact]
    public void For_TakesTheUsersOneGroup()
    {
        var user = Guid.NewGuid();
        var first = WithTemplate("First", t => { t.Access.ManageIsHidden = true; t.Access.IsHidden = true; }, user);
        var second = WithTemplate("Second", t =>
        {
            t.Access.ManageRemoteClientBitrateLimit = true;
            t.Access.RemoteClientBitrateLimit = 8_000_000;
        }, user);
        _config.Configuration.Groups.AddRange(new[] { first, second });

        var merged = PolicyTemplateMerger.For(_config.Configuration, user);
        Assert.True(merged.Access is { ManageIsHidden: true, IsHidden: true });
        Assert.False(merged.Access.ManageRemoteClientBitrateLimit);
        Assert.False(merged.Access.ManageLibraryAccess);
    }

    /// <summary>
    /// A row carries every value it guards, such as the library list of Library access.
    /// True: a merged Library access row keeps its libraries.
    /// False: a member could get All off with no libraries.
    /// </summary>
    [Fact]
    public void For_CopiesEveryValueOfARow()
    {
        var user = Guid.NewGuid();
        var library = Guid.NewGuid();
        _config.Configuration.Groups.Add(WithTemplate("Kids", t =>
        {
            t.Access.ManageLibraryAccess = true;
            t.Access.EnableAllFolders = false;
            t.Access.EnabledFolders = new List<Guid> { library };
            t.Access.ManageMaxParentalRating = true;
            t.Access.MaxParentalRating = 10;
            t.Access.MaxParentalSubRating = 1;
        }, user));

        var merged = PolicyTemplateMerger.For(_config.Configuration, user).Access;
        Assert.False(merged.EnableAllFolders);
        Assert.Equal(new[] { library }, merged.EnabledFolders);
        Assert.Equal(10, merged.MaxParentalRating);
        Assert.Equal(1, merged.MaxParentalSubRating);
    }

    /// <summary>
    /// A user in no group takes Everyone's rows.
    /// True: Everyone's Jellyfin settings cover every loose user.
    /// False: users in no group would never take them.
    /// </summary>
    [Fact]
    public void For_UserInNoGroup_TakesEveryones()
    {
        var user = Guid.NewGuid();
        Assert.False(PolicyTemplateRows.SetsAnything(PolicyTemplateMerger.For(_config.Configuration, user)));

        _config.Configuration.Everyone.Template.Access.ManageIsHidden = true;
        Assert.True(PolicyTemplateMerger.For(_config.Configuration, user).Access.ManageIsHidden);
    }

    /// <summary>
    /// Every switch of a template guards values that exist, and every value has a switch.
    /// True: no setting is left out of a merge.
    /// False: a setting could be set by a group and never reach a member.
    /// </summary>
    [Theory]
    [InlineData(typeof(Jellyfin.Plugin.Necto.Configuration.Policies.AccessTemplate))]
    [InlineData(typeof(Jellyfin.Plugin.Necto.Configuration.Policies.PreferencesTemplate))]
    public void Rows_CoverEveryValue(Type part)
    {
        var rows = PolicyTemplateRows.Of(part);
        var guarded = rows.SelectMany(r => r.Values.Select(v => v.Name)).ToHashSet();
        var values = part.GetProperties().Where(p => !p.Name.StartsWith("Manage", StringComparison.Ordinal)).Select(p => p.Name);
        Assert.All(values, v => Assert.Contains(v, guarded));
    }

    /// <summary>
    /// A template stored with its rows off loads with every row left to the member, and its values kept.
    /// True: a group saved with Jellyfin settings off never starts writing them.
    /// False: rows a group never applied would start changing members.
    /// </summary>
    [Fact]
    public void Normalizer_StoredOff_ClearsEveryRow()
    {
        var template = new Jellyfin.Plugin.Necto.Configuration.Policies.PolicyTemplate { Enabled = false };
        template.Access.ManageIsHidden = true;
        template.Access.IsHidden = true;
        template.Preferences.ManageSubtitleMode = true;

        PolicyTemplateNormalizer.Complete(template);

        Assert.True(template.Enabled);
        Assert.False(template.Access.ManageIsHidden);
        Assert.True(template.Access.IsHidden);
        Assert.False(template.Preferences.ManageSubtitleMode);
    }

    /// <summary>
    /// A stored configuration holding one group with Set Jellyfin settings off and one with it on loads, and only the
    /// group that had it on sets rows.
    /// True: saved groups keep doing what they did.
    /// False: an upgrade would start writing rows a group never applied, or drop rows it did.
    /// </summary>
    [Fact]
    public void StoredConfiguration_OldShape_LoadsAndKeepsWhatApplied()
    {
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Groups>
                <Group>
                  <Id>0b9a23b3-307c-4752-98d7-61a7538d8141</Id>
                  <Name>Off</Name>
                  <MemberIds><guid>9c5dc8c7-c137-4821-97bd-56dabad697d2</guid></MemberIds>
                  <Template>
                    <Enabled>false</Enabled>
                    <Access><ManageIsHidden>true</ManageIsHidden><IsHidden>true</IsHidden></Access>
                  </Template>
                </Group>
                <Group>
                  <Id>fd989b66-6edf-4259-89d0-73460ee2a064</Id>
                  <Name>On</Name>
                  <MemberIds><guid>1c5dc8c7-c137-4821-97bd-56dabad697d2</guid></MemberIds>
                  <Template>
                    <Enabled>true</Enabled>
                    <Access><ManageRemoteClientBitrateLimit>true</ManageRemoteClientBitrateLimit><RemoteClientBitrateLimit>8000000</RemoteClientBitrateLimit></Access>
                  </Template>
                </Group>
              </Groups>
            </PluginConfiguration>
            """;
        using var reader = new System.IO.StringReader(Xml.Trim());
        var config = (Jellyfin.Plugin.Necto.Configuration.PluginConfiguration)new System.Xml.Serialization.XmlSerializer(typeof(Jellyfin.Plugin.Necto.Configuration.PluginConfiguration)).Deserialize(reader)!;
        Assert.False(PolicyTemplateMerger.For(config, Guid.Parse("9c5dc8c7-c137-4821-97bd-56dabad697d2")).Access.ManageIsHidden);
        var on = PolicyTemplateMerger.For(config, Guid.Parse("1c5dc8c7-c137-4821-97bd-56dabad697d2")).Access;
        Assert.True(on is { ManageRemoteClientBitrateLimit: true, RemoteClientBitrateLimit: 8_000_000 });

        GroupNormalizer.Normalize(config);
        Assert.All(config.Groups, g => Assert.True(g.Template.Enabled));
        Assert.False(config.Groups[0].Template.Access.ManageIsHidden);
        Assert.True(config.Groups[0].Template.Access.IsHidden);
        Assert.True(config.Groups[1].Template.Access.ManageRemoteClientBitrateLimit);
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
    /// Preferences on join only are written when the user joins, and left alone on later syncs, while access still is.
    /// True: a member may change preferences the group gave them once.
    /// False: every sync would undo a member's own choices, or a new member would never get them.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_OnJoinPreferences_WrittenOnlyWhenJoining()
    {
        var viewer = _users.Add("viewer");
        _config.Configuration.Groups.Add(WithTemplate("Kids", t =>
        {
            t.PreferencesMode = Jellyfin.Plugin.Necto.Configuration.Policies.PreferencesMode.OnJoin;
            t.Access.ManageIsHidden = true;
            t.Access.IsHidden = true;
            t.Preferences.ManageSubtitleMode = true;
            t.Preferences.SubtitleMode = "Always";
        }, viewer.Id));

        Assert.True(await Service().ApplyAsync(viewer));
        Assert.True(_users.Policies[viewer.Id].IsHidden);
        Assert.False(_users.Configurations.ContainsKey(viewer.Id));

        await Service().ApplyAllAsync(null, default, new[] { viewer.Id });
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
