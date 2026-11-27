using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.MenuLinks;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.MenuLinks;

/// <summary>
/// The Menu links editor changes only the menuLinks of the web client's config.json, in the order given, and keeps
/// every other key where it was.
/// </summary>
public class MenuLinksFileTests
{
    private const string Web = """
        {
          "includeCorsCredentials": false,
          "menuLinks": [
            { "name": "Wiki", "icon": "help", "url": "https://wiki.example.com" },
            { "name": "Requests", "icon": "search", "url": "../requests/", "target": "_self" }
          ],
          "multiserver": false,
          "themes": [{ "name": "Dark", "id": "dark", "default": true }],
          "plugins": ["playAccessValidation/plugin"]
        }
        """;

    /// <summary>
    /// The links come back with their name, icon, and address, in order.
    /// True: the editor shows the menu as the web client draws it.
    /// False: the editor would show links out of order or without their icon.
    /// </summary>
    [Fact]
    public void LinksOf_ReadsEachLinkInOrder()
    {
        var links = MenuLinksFile.LinksOf(JsonNode.Parse(Web)!.AsObject());

        Assert.Equal(new[] { "Wiki", "Requests" }, links.Select(l => l.Name));
        Assert.Equal(new[] { "help", "search" }, links.Select(l => l.Icon));
        Assert.Equal(new[] { "https://wiki.example.com", "../requests/" }, links.Select(l => l.Url));
    }

    /// <summary>
    /// A file with no menuLinks reads as none, and entries that are not objects are skipped.
    /// True: a fresh web client opens with an empty list.
    /// False: the editor would fail on a config.json without links.
    /// </summary>
    [Fact]
    public void LinksOf_NoLinks_IsEmpty()
    {
        Assert.Empty(MenuLinksFile.LinksOf(JsonNode.Parse("{\"themes\": []}")!.AsObject()));
        Assert.Single(MenuLinksFile.LinksOf(JsonNode.Parse("{\"menuLinks\": [1, {\"name\": \"A\", \"url\": \"/a\"}]}")!.AsObject()));
    }

    /// <summary>
    /// Saving reorders, adds, and removes links, and every other key keeps its value and its place, with menuLinks in its own.
    /// True: the editor changes the menu and nothing else in config.json.
    /// False: saving the menu would lose or move the web client's other settings.
    /// </summary>
    [Fact]
    public void SetLinks_KeepsEveryOtherKeyAndItsPlace()
    {
        var web = JsonNode.Parse(Web)!.AsObject();
        var keysBefore = web.Select(p => p.Key).ToList();
        var themesBefore = web["themes"]!.ToJsonString();

        MenuLinksFile.SetLinks(web, new List<MenuLink>
        {
            new() { Name = "Requests", Icon = "movie", Url = "../requests/" },
            new() { Name = "Rules", Icon = "", Url = "../pages/rules" }
        });

        Assert.Equal(keysBefore, web.Select(p => p.Key).ToList());
        Assert.Equal(themesBefore, web["themes"]!.ToJsonString());
        Assert.False(web["includeCorsCredentials"]!.GetValue<bool>());
        Assert.Equal("playAccessValidation/plugin", web["plugins"]![0]!.GetValue<string>());
        var links = web["menuLinks"]!.AsArray();
        Assert.Equal(new[] { "../requests/", "../pages/rules" }, links.Select(l => l!["url"]!.GetValue<string>()));
        Assert.Equal("movie", links[0]!["icon"]!.GetValue<string>());
        Assert.Null(links[1]!["icon"]);
    }

    /// <summary>
    /// A key of a link the editor does not show, such as target, stays on the link to the same address.
    /// True: a hand added key survives a save.
    /// False: saving would strip what the administrator added by hand.
    /// </summary>
    [Fact]
    public void SetLinks_KeepsALinksOwnKeys()
    {
        var web = JsonNode.Parse(Web)!.AsObject();

        MenuLinksFile.SetLinks(web, new List<MenuLink> { new() { Name = "Ask", Icon = "search", Url = "../requests/" } });

        var link = Assert.Single(web["menuLinks"]!.AsArray())!.AsObject();
        Assert.Equal("_self", link["target"]!.GetValue<string>());
        Assert.Equal("Ask", link["name"]!.GetValue<string>());
    }

    /// <summary>
    /// A file with no menuLinks gains them at the end.
    /// True: the first link can be added to a fresh web client.
    /// False: a config.json without menuLinks could never get one.
    /// </summary>
    [Fact]
    public void SetLinks_NoLinks_AddsThemAtTheEnd()
    {
        var web = JsonNode.Parse("{\"multiserver\": false}")!.AsObject();

        MenuLinksFile.SetLinks(web, new List<MenuLink> { new() { Name = "Wiki", Icon = "help", Url = "https://wiki.example.com" } });

        Assert.Equal(new[] { "multiserver", "menuLinks" }, web.Select(p => p.Key));
    }

    /// <summary>
    /// A link needs a name and an address, and an icon is a Material icon name.
    /// True: the menu never holds a blank or broken link.
    /// False: a nameless link, or markup in the icon, would reach the web client.
    /// </summary>
    [Theory]
    [InlineData("Wiki", "help", "https://wiki.example.com", null)]
    [InlineData("Wiki", "", "/wiki", null)]
    [InlineData(" ", "help", "/wiki", "MenuLinksErrorNameRequired")]
    [InlineData("Wiki", "help", "", "MenuLinksErrorUrlRequired")]
    [InlineData("Wiki", "Help Me", "/wiki", "MenuLinksErrorIcon")]
    [InlineData("Wiki", "<b>", "/wiki", "MenuLinksErrorIcon")]
    public void ProblemOf_ChecksEachField(string name, string icon, string url, string? problem)
    {
        Assert.Equal(problem, MenuLinksFile.ProblemOf(new MenuLink { Name = name, Icon = icon, Url = url }));
    }

    /// <summary>
    /// Writing swaps the whole file in, and reading it back gives what was written, other keys and all.
    /// True: the web client reads a whole file after a save.
    /// False: a save could leave half a file or lose keys.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task WriteAsync_RoundTripsTheWholeFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "necto-menulinks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "config.json");
            await File.WriteAllTextAsync(path, Web);
            var web = (await MenuLinksFile.ReadAsync(path, CancellationToken.None))!;
            MenuLinksFile.SetLinks(web, new List<MenuLink> { new() { Name = "Wiki", Icon = "help", Url = "https://wiki.example.com" } });

            await MenuLinksFile.WriteAsync(path, web, CancellationToken.None);

            var back = (await MenuLinksFile.ReadAsync(path, CancellationToken.None))!;
            Assert.Equal(new[] { "includeCorsCredentials", "menuLinks", "multiserver", "themes", "plugins" }, back.Select(p => p.Key));
            Assert.Equal("Wiki", Assert.Single(MenuLinksFile.LinksOf(back)).Name);
            Assert.Single(Directory.GetFiles(folder));
            Assert.True(MenuLinksFile.CanWrite(path));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The links Requests and Issues keep are marked while each tool is on, and a save must keep a kept link.
    /// True: the editor labels the Requests link and refuses to drop it while Requests is on.
    /// False: the Requests link could be removed while Requests is on, or stay marked once it is off.
    /// </summary>
    [Fact]
    public void Managed_RequestsAndIssues_AreKeptWhileOn()
    {
        var links = new List<MenuLink>
        {
            new() { Name = "Requests", Url = "../requests/" },
            new() { Name = "Issues", Url = "/issues" },
            new() { Name = "Other requests", Url = "https://seerr.example.com/requests" }
        };

        MenuLinksService.MarkManaged(links, requestsOn: true, issuesOn: false);
        Assert.Equal(new string?[] { "requests", null, null }, links.Select(l => l.Managed));
        MenuLinksService.MarkManaged(links, requestsOn: false, issuesOn: true);
        Assert.Equal(new string?[] { null, "issues", null }, links.Select(l => l.Managed));

        var without = links.Skip(1).ToList();
        Assert.Equal("MenuLinksErrorKeepRequests", MenuLinksService.KeepsManaged(links, without, requestsOn: true, issuesOn: false));
        Assert.Null(MenuLinksService.KeepsManaged(links, without, requestsOn: false, issuesOn: false));
        Assert.Equal("MenuLinksErrorKeepIssues", MenuLinksService.KeepsManaged(links, links.Take(1).ToList(), requestsOn: true, issuesOn: true));
        Assert.Null(MenuLinksService.KeepsManaged(without, without, requestsOn: true, issuesOn: false));
    }
}
