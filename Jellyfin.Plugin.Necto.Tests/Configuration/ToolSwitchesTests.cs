using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration;

/// <summary>
/// One list names the tools that turn on and off, each read from its own settings section, and the pages keep the same
/// list.
/// </summary>
public class ToolSwitchesTests
{
    /// <summary>
    /// The nine optional tools have a switch, in sidebar order, and the always on tools have none.
    /// True: Requests through Storage turn on and off, and Dashboard, Groups, Notifications, Settings, and Troubleshooting stay.
    /// False: a tool would have no switch, or an always on tool could be turned off.
    /// </summary>
    [Fact]
    public void All_ListsTheOptionalToolsInSidebarOrder()
    {
        var keys = ToolSwitches.All.Select(t => t.Key).ToList();

        Assert.Equal(new[] { "requests", "issues", "invites", "proxy", "ddns", "wireguard", "scripts", "pages", "storage" }, keys);
        var sidebar = Plugin.ToolKeys.Where(keys.Contains).ToList();
        Assert.Equal(keys, sidebar);
        foreach (var always in new[] { "dashboard", "groups", "notifications", "settings", "troubleshooting" })
        {
            Assert.DoesNotContain(always, keys);
            Assert.True(ToolSwitches.IsOn(new PluginConfiguration(), always));
        }
    }

    /// <summary>
    /// A fresh install has every optional tool off.
    /// True: a new server runs no optional tool until it is turned on.
    /// False: a tool would start working before the administrator chose it.
    /// </summary>
    [Fact]
    public void FreshInstall_HasEveryOptionalToolOff()
    {
        var config = new PluginConfiguration();

        Assert.All(ToolSwitches.All, tool => Assert.False(ToolSwitches.IsOn(config, tool.Key)));
    }

    /// <summary>
    /// Each switch is the Enabled of the section it names, and turning one on leaves the others off.
    /// True: each tool reads its own switch.
    /// False: a tool would follow another tool's switch.
    /// </summary>
    [Fact]
    public void IsOn_ReadsEachToolsOwnSection()
    {
        foreach (var tool in ToolSwitches.All)
        {
            var config = new PluginConfiguration();
            var section = typeof(PluginConfiguration).GetProperty(tool.Section)!.GetValue(config)!;
            section.GetType().GetProperty("Enabled")!.SetValue(section, true);

            Assert.True(ToolSwitches.IsOn(config, tool.Key), tool.Key);
            Assert.Equal(new[] { tool.Key }, ToolSwitches.All.Where(t => ToolSwitches.IsOn(config, t.Key)).Select(t => t.Key));
        }
    }

    /// <summary>
    /// The pages' TOOL_SWITCHES in necto_shared.js names the same tools, in the same order, with the same sections.
    /// True: the sidebar, the tiles, and the server agree on which tools are on.
    /// False: a tool could leave the sidebar while its routes still work, or the other way round.
    /// </summary>
    [Fact]
    public void PagesList_MatchesTheServers()
    {
        var script = EmbeddedResources.Read("necto_shared.js");
        var block = Regex.Match(script, @"export var TOOL_SWITCHES = \{(?<body>[^}]*)\};", RegexOptions.Singleline);
        Assert.True(block.Success);
        var pairs = Regex.Matches(block.Groups["body"].Value, @"(?<key>\w+): '(?<section>\w+)'")
            .Select(m => (m.Groups["key"].Value, m.Groups["section"].Value))
            .ToList();

        Assert.Equal(ToolSwitches.All.Select(t => (t.Key, t.Section)).ToList(), pairs);
    }

    /// <summary>
    /// Turning a tool off keeps what it stored: the switch is a flag beside its data.
    /// True: a tool turned back on finds its records, settings, and history as they were.
    /// False: turning a tool off would lose its data.
    /// </summary>
    [Fact]
    public void TurningOff_KeepsTheToolsData()
    {
        var config = new PluginConfiguration();
        config.Scripts.Enabled = true;
        config.Scripts.Scripts.Add(new Jellyfin.Plugin.Necto.Configuration.Scripts.ScriptDefinition { Name = "Kept" });
        config.Scripts.Enabled = false;

        Assert.False(ToolSwitches.IsOn(config, ToolSwitches.Scripts));
        Assert.Equal("Kept", Assert.Single(config.Scripts.Scripts).Name);
    }
}
