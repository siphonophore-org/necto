using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests;

/// <summary>
/// The plugin names itself, keeps its id, and registers the Necto page with every tool and every file it serves.
/// </summary>
public class PluginTests
{
    private static readonly string[] Tools =
    {
        "dashboard", "requests", "issues", "invites", "groups", "proxy", "ddns",
        "wireguard", "scripts", "pages", "storage", "notifications", "settings", "troubleshooting"
    };

    /// <summary>
    /// The plugin is named Necto.
    /// True: the dashboard and the catalog show the same name.
    /// False: the plugin shows under another name than its manifest's.
    /// </summary>
    [Fact]
    public void Name_IsNecto()
    {
        var plugin = TestPlugin.Create();

        Assert.Equal("Necto", plugin.Name);
    }

    /// <summary>
    /// The plugin keeps the id in build.yaml.
    /// True: Jellyfin matches the installed plugin to its catalog entry.
    /// False: an update installs beside the old copy instead of replacing it.
    /// </summary>
    [Fact]
    public void Id_MatchesTheManifest()
    {
        var plugin = TestPlugin.Create();

        Assert.Equal(Guid.Parse("a8b5ecb0-fc00-428b-85e8-74b85820166d"), plugin.Id);
    }

    /// <summary>
    /// The tool keys are the 14 tools, in sidebar order.
    /// True: the registered pages match the tools the sidebar lists.
    /// False: a tool in the sidebar would open a page that is not registered.
    /// </summary>
    [Fact]
    public void ToolKeys_AreEveryToolInOrder()
    {
        Assert.Equal(Tools, Plugin.ToolKeys);
    }

    /// <summary>
    /// Only the necto page shows in the dashboard menu.
    /// True: the menu has one Necto entry, which holds every tool.
    /// False: each tool would show as its own menu entry, or the page would be missing from the menu.
    /// </summary>
    [Fact]
    public void GetPages_OnlyTheHostPage_IsInTheMenu()
    {
        var pages = TestPlugin.Create().GetPages().ToList();

        var menu = Assert.Single(pages, p => p.EnableInMainMenu);
        Assert.Equal("necto", menu.Name);
        Assert.Equal("Necto", menu.DisplayName);
    }

    /// <summary>
    /// Every tool registers its page and its script, and the shared files and Stolon's assets are registered.
    /// True: the tool app finds each tool's page by its name.
    /// False: selecting a tool would load nothing.
    /// </summary>
    [Fact]
    public void GetPages_EveryTool_IsRegistered()
    {
        var names = TestPlugin.Create().GetPages().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var expected = new List<string> { "necto", "necto.js", "necto_shared.js", "necto_shared.css", "necto_groups_sections.js", "necto_stolon.js", "necto_stolon.css" };
        expected.AddRange(Tools.SelectMany(t => new[] { "necto_" + t, "necto_" + t + ".js" }));

        Assert.All(expected, n => Assert.Contains(n, names));
    }

    /// <summary>
    /// Every registered page points at a resource the assembly embeds.
    /// True: Jellyfin serves each page it lists.
    /// False: a page would answer with nothing once installed.
    /// </summary>
    [Fact]
    public void GetPages_EveryPage_IsEmbedded()
    {
        var embedded = typeof(Plugin).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);

        var missing = TestPlugin.Create().GetPages().Where(p => !embedded.Contains(p.EmbeddedResourcePath)).Select(p => p.Name).ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Every file in Web/ is embedded and registered as a page.
    /// True: each file a page loads is served.
    /// False: a file added to Web/ would never reach the browser.
    /// </summary>
    [Fact]
    public void WebFolder_EveryFile_IsEmbeddedAndRegistered()
    {
        var embedded = typeof(Plugin).Assembly.GetManifestResourceNames().ToHashSet(StringComparer.Ordinal);
        var registered = TestPlugin.Create().GetPages().Select(p => p.EmbeddedResourcePath).ToHashSet(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(Path.Combine(ProjectFolder(), "Web")).Select(f => "Jellyfin.Plugin.Necto.Web." + Path.GetFileName(f)).ToList();

        Assert.Equal(35, files.Count);
        Assert.All(files, f => Assert.Contains(f, embedded));
        Assert.All(files, f => Assert.Contains(f, registered));
    }

    /// <summary>
    /// Finds the plugin's project folder from this file's own path.
    /// </summary>
    private static string ProjectFolder([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "Jellyfin.Plugin.Necto"));
}
