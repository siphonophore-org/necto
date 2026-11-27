using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The menu link installer adds the Requests and Issues links to the web client's menu once, takes the Issues link out
/// again, and writes the web client's config.json whole.
/// </summary>
public class MenuLinkInstallerTests
{
    /// <summary>
    /// The Requests link is added after the links already there, and the rest of the file is kept.
    /// True: the web client's menu gains the Requests page without losing anything.
    /// False: the menu would lose its own links or the file its other settings.
    /// </summary>
    [Fact]
    public void AddLink_ExistingLinks_AddsAfterThem()
    {
        var web = JsonNode.Parse("{\"menuLinks\": [{\"name\": \"Wiki\", \"icon\": \"help\", \"url\": \"https://example.com\"}], \"themes\": []}")!.AsObject();

        Assert.True(MenuLinkInstaller.AddLink(web));

        Assert.Equal(new[] { "https://example.com", "../requests/" }, web["menuLinks"]!.AsArray().Select(l => l!["url"]!.GetValue<string>()).ToArray());
        Assert.Equal("movie_filter", web["menuLinks"]![1]!["icon"]!.GetValue<string>());
        Assert.NotNull(web["themes"]);
    }

    /// <summary>
    /// The menu links are made when there are none.
    /// True: a fresh web client gets the Requests link.
    /// False: a config.json without menuLinks would never gain the link.
    /// </summary>
    [Fact]
    public void AddLink_NoLinks_MakesThem()
    {
        var web = new JsonObject();

        Assert.True(MenuLinkInstaller.AddLink(web));

        Assert.Single(web["menuLinks"]!.AsArray());
    }

    /// <summary>
    /// A link already going to this server's request page is left alone.
    /// True: the link an administrator renamed or moved is not added twice.
    /// False: the menu would show the Requests page twice.
    /// </summary>
    [Theory]
    [InlineData("../requests/")]
    [InlineData("/requests")]
    [InlineData("/jellyfin/requests/#/mine")]
    [InlineData("REQUESTS")]
    public void AddLink_LinkToThePage_IsLeftAlone(string url)
    {
        var web = JsonNode.Parse("{\"menuLinks\": [{\"name\": \"Ask\", \"icon\": \"search\", \"url\": \"" + url + "\"}]}")!.AsObject();

        Assert.False(MenuLinkInstaller.AddLink(web));

        Assert.Single(web["menuLinks"]!.AsArray());
    }

    /// <summary>
    /// A link with a host, or to another page, is not this server's request page.
    /// True: a menu linking a Jellyseerr still gains this server's own link.
    /// False: another site's page named requests would stop the link being added.
    /// </summary>
    [Theory]
    [InlineData("https://seerr.example.com/requests")]
    [InlineData("https://media.example.com/requests/?x=1")]
    [InlineData("//seerr.example.com/requests")]
    [InlineData("../requests-old/")]
    [InlineData("")]
    public void GoesToRequests_AnotherSitesPage_DoesNotCount(string url)
    {
        var web = JsonNode.Parse("{\"menuLinks\": [{\"name\": \"Seerr\", \"icon\": \"search\", \"url\": \"" + url + "\"}]}")!.AsObject();

        Assert.False(MenuLinkInstaller.GoesToRequests(url));
        Assert.True(MenuLinkInstaller.AddLink(web));
        Assert.Equal(2, web["menuLinks"]!.AsArray().Count);
    }

    /// <summary>
    /// The new config.json replaces the old one whole, with no temp file left beside it.
    /// True: the web client never reads a half written file.
    /// False: a crash mid write would leave a broken config.json, or a stray temp file.
    /// </summary>
    [Fact]
    public async Task WriteAsync_Folder_ReplacesTheFileWhole()
    {
        var folder = Directory.CreateTempSubdirectory("necto-menu-").FullName;
        try
        {
            var path = Path.Combine(folder, "config.json");
            await File.WriteAllTextAsync(path, "{\"old\": true}");

            await MenuLinkInstaller.WriteAsync(path, "{\"menuLinks\": []}", CancellationToken.None);

            Assert.Equal("{\"menuLinks\": []}", await File.ReadAllTextAsync(path));
            Assert.Equal(new[] { path }, Directory.GetFiles(folder));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A config.json whose folder cannot be written, as when Docker mounts the file alone, is written in place.
    /// True: the link is still added on such a server.
    /// False: the write would fail on a mounted file.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ReadOnlyFolder_WritesInPlace()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Directory.CreateTempSubdirectory("necto-menu-").FullName;
        var path = Path.Combine(folder, "config.json");
        await File.WriteAllTextAsync(path, "{\"old\": true}");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await MenuLinkInstaller.WriteAsync(path, "{\"menuLinks\": []}", CancellationToken.None);

            Assert.Equal("{\"menuLinks\": []}", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The Issues link is added once, and taken out again with every link to the issue page, leaving Requests.
    /// True: turning Issues on and off adds and removes its menu link only.
    /// False: the link would be added twice, or removing it would take the Requests link too.
    /// </summary>
    [Fact]
    public void AddIssuesLink_OnAndOff_AddsOnceAndRemoves()
    {
        var web = JsonNode.Parse("{\"menuLinks\": [{\"name\": \"Requests\", \"icon\": \"search\", \"url\": \"../requests/\"}]}")!.AsObject();

        Assert.True(MenuLinkInstaller.AddIssuesLink(web));
        Assert.False(MenuLinkInstaller.AddIssuesLink(web));
        Assert.Equal(new[] { "../requests/", "../issues/" }, web["menuLinks"]!.AsArray().Select(l => l!["url"]!.GetValue<string>()));
        Assert.Equal("Issues", web["menuLinks"]![1]!["name"]!.GetValue<string>());
        Assert.Equal("report_problem", web["menuLinks"]![1]!["icon"]!.GetValue<string>());

        web["menuLinks"]!.AsArray().Add(new JsonObject { ["name"] = "Report", ["url"] = "/jellyfin/issues" });
        Assert.True(MenuLinkInstaller.RemoveIssuesLink(web));
        Assert.Equal(new[] { "../requests/" }, web["menuLinks"]!.AsArray().Select(l => l!["url"]!.GetValue<string>()));
        Assert.False(MenuLinkInstaller.RemoveIssuesLink(web));
    }

    /// <summary>
    /// Only this server's issue page counts as one.
    /// True: a tracker's issues page in the menu is left alone.
    /// False: turning Issues off would remove another site's link.
    /// </summary>
    [Theory]
    [InlineData("https://tracker.example.com/issues", false)]
    [InlineData("../issues/", true)]
    [InlineData("ISSUES", true)]
    [InlineData("../requests/", false)]
    public void GoesTo_IssuePage_OnlyThisServers(string url, bool counts)
        => Assert.Equal(counts, MenuLinkInstaller.GoesTo(url, "issues"));

    /// <summary>
    /// The Requests and Issues links are added with the name and icon set in Menu links, or their defaults.
    /// True: a link renamed in Menu links comes back renamed after its tool is turned off and on.
    /// False: turning a tool off and on would undo the administrator's name and icon.
    /// </summary>
    [Fact]
    public void AddLink_UsesTheNameAndIconSetInMenuLinks()
    {
        var web = JsonNode.Parse("{}")!.AsObject();

        Assert.True(MenuLinkInstaller.AddLink(web, "Ask for it", "movie"));
        Assert.True(MenuLinkInstaller.AddIssuesLink(web, " ", null));

        var links = web["menuLinks"]!.AsArray();
        Assert.Equal(new[] { "Ask for it", "Issues" }, links.Select(l => l!["name"]!.GetValue<string>()));
        Assert.Equal(new[] { "movie", "report_problem" }, links.Select(l => l!["icon"]!.GetValue<string>()));
    }

    /// <summary>
    /// Turning Requests off takes every link to the request page out, and leaves links elsewhere.
    /// True: the menu has no Requests link while Requests is off.
    /// False: the menu would link to a page that answers not found.
    /// </summary>
    [Fact]
    public void RemoveLink_TakesOutOnlyTheRequestPage()
    {
        var web = JsonNode.Parse("{\"menuLinks\": [{\"name\": \"Requests\", \"url\": \"../requests/\"}, {\"name\": \"Seerr\", \"url\": \"https://seerr.example.com/requests\"}, {\"name\": \"Again\", \"url\": \"/requests\"}]}")!.AsObject();

        Assert.True(MenuLinkInstaller.RemoveLink(web));
        Assert.False(MenuLinkInstaller.RemoveLink(web));

        Assert.Equal("Seerr", Assert.Single(web["menuLinks"]!.AsArray())!["name"]!.GetValue<string>());
    }
}
