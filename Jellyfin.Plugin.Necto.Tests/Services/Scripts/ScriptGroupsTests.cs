using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Scripts;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Scripts;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Scripts;

/// <summary>
/// A script runs for every user unless it names groups, then for their members, for an administrator always, and for an
/// event with no user when it names Everyone. Scripts stored groups ran move onto the scripts.
/// </summary>
public class ScriptGroupsTests
{
    private static ScriptDefinition Script(bool? all, params Guid[] groups)
        => new() { Id = Guid.NewGuid(), Name = "Test", AllGroups = all, GroupIds = groups.ToList() };

    /// <summary>
    /// A script for every user runs for anyone, and a script that names groups runs only for their members.
    /// True: the script editor's groups decide who a script runs for.
    /// False: a filtered script would run for everyone, or an open one for no one.
    /// </summary>
    [Fact]
    public void RunsFor_FollowsTheScriptsGroups()
    {
        var users = new FakeUsers();
        var member = users.Add("viewer");
        var outsider = users.Add("guest");
        var admin = users.Add("admin", admin: true);
        var family = TestGroups.Make("Family", member.Id);
        var config = TestGroups.Config(family);
        var open = Script(true);
        var filtered = Script(false, family.Id);
        config.Scripts.Scripts.AddRange(new[] { open, filtered });
        var store = new FakeConfigurationStore(config);
        var groups = new ScriptGroups(new PermissionService(store, users.Manager), store);

        Assert.True(groups.RunsFor(member.Id, open.Id));
        Assert.True(groups.RunsFor(outsider.Id, open.Id));
        Assert.True(groups.RunsFor(member.Id, filtered.Id));
        Assert.False(groups.RunsFor(outsider.Id, filtered.Id));
        Assert.True(groups.RunsFor(admin.Id, filtered.Id));
    }

    /// <summary>
    /// An event with no user, and a user in no group, take Everyone.
    /// True: Everyone in a script's groups stands for users in no group.
    /// False: such an event would run every filtered script, or none.
    /// </summary>
    [Fact]
    public void RunsFor_NoUserOrNoGroup_TakesEveryone()
    {
        var users = new FakeUsers();
        var loose = users.Add("loose");
        var store = new FakeConfigurationStore();
        var script = Script(false);
        store.Configuration.Scripts.Scripts.Add(script);
        var groups = new ScriptGroups(new PermissionService(store, users.Manager), store);

        Assert.False(groups.RunsFor(Guid.Empty, script.Id));
        Assert.False(groups.RunsFor(loose.Id, script.Id));
        script.GroupIds.Add(Group.EveryoneId);
        Assert.True(groups.RunsFor(Guid.Empty, script.Id));
        Assert.True(groups.RunsFor(loose.Id, script.Id));
    }

    /// <summary>
    /// A stored script takes the groups that ran it, and runs for every user when every group or no group ran it. The
    /// groups' own picks are cleared, and a second run changes nothing.
    /// True: an upgrade keeps each script running for the same members, and an unassigned script runs for everyone.
    /// False: an upgrade would widen or narrow who a script runs for.
    /// </summary>
    [Fact]
    public void Migrate_MovesGroupPicksOntoScripts()
    {
        var some = Script(null);
        var every = Script(null);
        var none = Script(null);
        var family = TestGroups.Make("Family", p => p.Scripts.ScriptIds.Add(some.Id.ToString("N")));
        var kids = TestGroups.Make("Kids", p => p.Scripts.ScriptIds.Add(some.Id.ToString()));
        var config = TestGroups.Config(family, kids);
        config.Everyone.Permissions.Scripts.ScriptIds.Add(every.Id.ToString());
        family.Permissions.Scripts.ScriptIds.Add(every.Id.ToString());
        kids.Permissions.Scripts.ScriptIds.Add(every.Id.ToString());
        config.Scripts.Scripts.AddRange(new[] { some, every, none });

        Assert.True(ScriptGroupFilter.Migrate(config));

        Assert.False(some.AllGroups);
        Assert.Equal(new[] { family.Id, kids.Id }.OrderBy(g => g), some.GroupIds.OrderBy(g => g));
        Assert.True(every.AllGroups);
        Assert.Empty(every.GroupIds);
        Assert.False(none.AllGroups is false);
        Assert.All(config.Groups.Append(config.Everyone), g => Assert.True(g.Permissions.Scripts is { AllScripts: false, ScriptIds.Count: 0 }));
        Assert.False(ScriptGroupFilter.Migrate(config));
    }

    /// <summary>
    /// A script sent without its groups keeps the stored ones, and a removed group leaves a script's groups.
    /// True: a save from an older page never opens a filtered script to everyone.
    /// False: a save could widen who a script runs for.
    /// </summary>
    [Fact]
    public void Migrate_KeepsStoredGroupsAndDropsRemovedOnes()
    {
        var family = TestGroups.Make("Family");
        var stored = TestGroups.Config(family);
        var script = Script(false, family.Id);
        stored.Scripts.Scripts.Add(script);
        var incoming = TestGroups.Config(family);
        incoming.Scripts.Scripts.Add(new ScriptDefinition { Id = script.Id, Name = "Test" });

        ScriptGroupFilter.Migrate(incoming, stored);
        Assert.False(incoming.Scripts.Scripts[0].AllGroups);
        Assert.Equal(new[] { family.Id }, incoming.Scripts.Scripts[0].GroupIds);

        incoming.Groups.Clear();
        ScriptGroupFilter.Migrate(incoming);
        Assert.Empty(incoming.Scripts.Scripts[0].GroupIds);
        Assert.False(incoming.Scripts.Scripts[0].AllGroups);
    }

    /// <summary>
    /// A stored configuration from before scripts held their groups loads with no AllGroups, and an empty group list
    /// round trips as empty.
    /// True: the upgrade can tell a stored script from one already moved.
    /// False: every stored script would read as moved, or a moved one as stored.
    /// </summary>
    [Fact]
    public void StoredConfiguration_OldShape_ReadsAsNotMoved()
    {
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Scripts><Scripts><ScriptDefinition><Id>0b9a23b3-307c-4752-98d7-61a7538d8141</Id><Name>Old</Name></ScriptDefinition></Scripts></Scripts>
            </PluginConfiguration>
            """;
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var config = (PluginConfiguration)serializer.Deserialize(new StringReader(Xml.Trim()))!;
        Assert.Null(config.Scripts.Scripts[0].AllGroups);

        ScriptGroupFilter.Migrate(config);
        using var buffer = new StringWriter();
        serializer.Serialize(buffer, config);
        var again = (PluginConfiguration)serializer.Deserialize(new StringReader(buffer.ToString()))!;
        Assert.True(again.Scripts.Scripts[0].AllGroups);
        Assert.NotNull(again.Scripts.Scripts[0].GroupIds);
        Assert.Empty(again.Scripts.Scripts[0].GroupIds);
    }
}
