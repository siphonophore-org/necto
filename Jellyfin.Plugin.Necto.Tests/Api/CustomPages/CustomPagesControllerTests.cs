using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api.CustomPages;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.CustomPages;

/// <summary>
/// The Pages controller decides who opens a page, from its tier and the caller's groups, and hardens every
/// page it serves.
/// </summary>
public class CustomPagesControllerTests
{
    private const string PageId = "page-1";

    private static (CustomPagesController Controller, StubPageService Pages) Create()
        => Create(Guid.NewGuid());

    private static (CustomPagesController Controller, StubPageService Pages) Create(Guid callerId, bool isApiKey = false, bool admin = false, PluginConfiguration? config = null)
    {
        var pages = new StubPageService();
        var user = callerId == Guid.Empty ? null : TestController.User(callerId, admin);
        var controller = TestController.Create(pages: pages, authorization: TestController.Authorization(user, isApiKey), config: config);
        return (controller, pages);
    }

    private static PluginConfiguration GroupGiving(Guid member, Action<CustomPagePermissions> give)
    {
        var group = TestGroups.Make("Family", p => give(p.CustomPages), member);
        return TestGroups.Config(group);
    }

    private static CustomPage Page(PageVisibility visibility, string slug = "p")
        => new CustomPage { Id = PageId, Slug = slug, Visibility = visibility, Enabled = true };

    private static PageAsset Asset(PageVisibility visibility, string name = "logo.png")
        => new PageAsset { Name = name, Visibility = visibility, ContentType = "image/png" };

    // MARK: Response hardening

    [Fact]
    public void Get_AnonymousPage_AppliesHardeningHeaders()
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);
        pages.RenderHtml = "<html></html>";

        controller.Get("p");
        var headers = controller.Response.Headers;

        Assert.False(string.IsNullOrEmpty(headers["Content-Security-Policy"]));
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("noindex, nofollow", headers["X-Robots-Tag"]);
    }

    [Fact]
    public void Get_AllowsEmbeddingBySameOriginOnly()
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);
        pages.RenderHtml = "<html></html>";

        controller.Get("p");

        Assert.Equal("SAMEORIGIN", controller.Response.Headers["X-Frame-Options"]);
        Assert.Contains(
            "frame-ancestors 'self'",
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Author content runs under one policy on every path. A srcdoc frame inherits its embedder's policy, and the page
    /// the loading screen writes in keeps the screen's header and adds its own meta policy.
    /// True: the screen's header with the page's meta policy holds every directive the anonymous path sends.
    /// False: the same page behaves differently by tier.
    /// </summary>
    [Fact]
    public async Task Csp_IsTheSameForAuthorContentOnEveryPath()
    {
        var (anonController, anonPages) = Create();
        anonPages.Pages["a"] = Page(PageVisibility.Anonymous, "a");
        anonController.Get("a");

        var (shellController, shellPages) = Create();
        shellPages.Pages["b"] = Page(PageVisibility.User, "b");
        shellController.Get("b");

        var (userController, userPages) = Create(Guid.NewGuid(), admin: true);
        userPages.Pages["c"] = Page(PageVisibility.User, "c");
        await userController.UserContent("c");

        var anonCsp = anonController.Response.Headers["Content-Security-Policy"].ToString();
        var screenCsp = shellController.Response.Headers["Content-Security-Policy"].ToString();
        var written = Directives(screenCsp + "; object-src 'none'; base-uri 'none'");
        Assert.Equal(Directives(anonCsp), written);
        Assert.Equal(anonCsp, userController.Response.Headers["Content-Security-Policy"].ToString());
    }

    /// <summary>
    /// The web client screens leave base-uri open, since their frame sets its base address to the server root.
    /// True: the screens load the web client's stylesheets and Stolon's script.
    /// False: the loading and not found screens show unstyled and never load the page.
    /// </summary>
    [Fact]
    public void Csp_LetsTheScreensSetTheirBase()
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.User);

        controller.Get("p");

        Assert.DoesNotContain("base-uri", controller.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", controller.Response.Headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
    }

    private static string[] Directives(string csp)
        => csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Csp_DoesNotRestrictFormSubmission()
    {
        // form-action 'none' silently broke every form on an anonymous page while the shell allowed
        // them, so the unified policy must not reintroduce the directive on one path only.
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);
        pages.RenderHtml = "<html></html>";

        controller.Get("p");

        Assert.DoesNotContain(
            "form-action",
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("object-src 'none'")]
    [InlineData("base-uri 'none'")]
    public void Csp_KeepsDirectivesThatProtectTheFramingDocument(string directive)
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);
        pages.RenderHtml = "<html></html>";

        controller.Get("p");

        Assert.Contains(
            directive,
            controller.Response.Headers["Content-Security-Policy"].ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ServesHtmlAsUtf8()
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);
        pages.RenderHtml = "<html></html>";

        var result = controller.Get("p");

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
    }

    // MARK: Entry point routing

    [Fact]
    public void Get_UnknownSlug_Returns404WithHardenedNotFoundBody()
    {
        var (controller, pages) = Create();
                
        var result = controller.Get("nope");

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        Assert.Contains("stolon-not-found", result.Content, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(controller.Response.Headers["Content-Security-Policy"]));
        Assert.Equal("SAMEORIGIN", controller.Response.Headers["X-Frame-Options"]);
    }

    /// <summary>
    /// An Anyone page gets the web client's content style.
    /// True: a page with no CSS of its own reads in the web client's look.
    /// False: the author's page shows black serif text on the dark background.
    /// </summary>
    [Fact]
    public void Get_AnonymousPage_RendersWithTheWebClientContentStyle()
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(PageVisibility.Anonymous);

        controller.Get("p");

        Assert.Contains("stolon-web-content", pages.LastContentStyle, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PageVisibility.User)]
    [InlineData(PageVisibility.Admin)]
    public void Get_ProtectedPage_ServesLoadingScreenRatherThanContent(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(visibility);
        pages.Pages["p"].Title = "Family & friends";

        var result = controller.Get("p");

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Contains("data-slug=\"p\"", result.Content, StringComparison.Ordinal);
        Assert.Contains("necto_pages_loader.js", result.Content, StringComparison.Ordinal);
        Assert.Contains("<title>Family &amp; friends</title>", result.Content, StringComparison.Ordinal);
        Assert.Equal(0, pages.Renders);
    }

    /// <summary>
    /// The loading screen is the web client's signed out frame named after the page, and an untitled page keeps the
    /// server's name.
    /// True: the tab shows the page's title from the start, and the frame never follows a sign-in.
    /// False: the tab reads Loading, or the frame keeps working after the page is written over it.
    /// </summary>
    [Fact]
    public void LoadingPage_IsTheSignedOutFrameNamedAfterThePage()
    {
        var named = CustomPagesController.LoadingPage("Family");
        var untitled = CustomPagesController.LoadingPage(" ");

        Assert.Equal(HostedPageFrame.WebClientSignedOut, named.Frame);
        Assert.Equal("Family", named.Title);
        Assert.Null(untitled.Title);
    }

    [Fact]
    public void Get_ProtectedPage_ShellIsInTheBrowsersPreferredLanguage()
    {
        var (controller, pages) = Create();
        controller.Request.Headers.AcceptLanguage = "en;q=0.5, de-DE, fr;q=0.8";
        pages.Pages["p"] = Page(PageVisibility.User);

        controller.Get("p");

        Assert.Equal(new[] { ("p", PageVisibility.User, (string?)"de-de") }, pages.Loaders);
    }

    [Fact]
    public void Get_ProtectedPage_ShellFollowsThePagesLanguageHeaderOverTheBrowsers()
    {
        var (controller, pages) = Create();
        controller.Request.Headers.AcceptLanguage = "fr";
        controller.Request.Headers["X-Jellyfin-Plugin-Language"] = "de";
        pages.Pages["p"] = Page(PageVisibility.User);

        controller.Get("p");

        Assert.Equal(new[] { ("p", PageVisibility.User, (string?)"de") }, pages.Loaders);
    }

    // MARK: Tier gating on the content endpoints

    [Theory]
    [InlineData(PageVisibility.Anonymous)]
    [InlineData(PageVisibility.Admin)]
    public async Task UserContent_RefusesPagesThatAreNotUserTier(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(visibility);

        Assert.IsType<NotFoundResult>(await controller.UserContent("p"));
        Assert.Equal(0, pages.Renders);
    }

    [Theory]
    [InlineData(PageVisibility.Anonymous)]
    [InlineData(PageVisibility.User)]
    public async Task AdminContent_RefusesPagesThatAreNotAdminTier(PageVisibility visibility)
    {
        var (controller, pages) = Create();
        pages.Pages["p"] = Page(visibility);

        Assert.IsType<NotFoundResult>(await controller.AdminContent("p"));
        Assert.Equal(0, pages.Renders);
    }

    [Fact]
    public async Task ContentEndpoints_ReturnNotFoundForUnknownSlug()
    {
        var (controller, pages) = Create();
        
        Assert.IsType<NotFoundResult>(await controller.UserContent("nope"));
        Assert.IsType<NotFoundResult>(await controller.AdminContent("nope"));
    }

    // MARK: Group access (security invariant)

    /// <summary>
    /// A Signed in page opens for a member whose group lists it.
    /// True: the group's grant reaches its members.
    /// False: a member a group gives the page to is refused.
    /// </summary>
    [Fact]
    public async Task UserContent_ServesAPageTheCallersGroupGives()
    {
        var caller = Guid.NewGuid();
        var (controller, pages) = Create(caller, config: GroupGiving(caller, p => p.PageIds.Add(PageId)));
        pages.Pages["p"] = Page(PageVisibility.User);

        Assert.IsType<ContentResult>(await controller.UserContent("p"));
        Assert.Equal(1, pages.Renders);
    }

    /// <summary>
    /// A group with Open every page opens every Signed in page.
    /// True: the all pages grant needs no list.
    /// False: a member of such a group is refused.
    /// </summary>
    [Fact]
    public async Task UserContent_ServesEveryPageWhenTheGroupGivesAll()
    {
        var caller = Guid.NewGuid();
        var (controller, pages) = Create(caller, config: GroupGiving(caller, p => p.AllPages = true));
        pages.Pages["p"] = Page(PageVisibility.User);

        Assert.IsType<ContentResult>(await controller.UserContent("p"));
    }

    /// <summary>
    /// A signed in user whose groups do not give the page is refused before the page is composed.
    /// True: a page is private to the groups that give it.
    /// False: any signed in user reads every Signed in page.
    /// </summary>
    [Fact]
    public async Task UserContent_RefusesACallerNoGroupGives()
    {
        var caller = Guid.NewGuid();
        var (controller, pages) = Create(caller, config: GroupGiving(caller, p => p.PageIds.Add("another-page")));
        pages.Pages["p"] = Page(PageVisibility.User);

        var result = Assert.IsType<StatusCodeResult>(await controller.UserContent("p"));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);

        // The refusal has to happen before the body is composed, or the restriction is cosmetic.
        Assert.Equal(0, pages.Renders);
    }

    /// <summary>
    /// A user in no group takes Everyone, which gives no page on a fresh install.
    /// True: a page opens only once a group gives it.
    /// False: a new user reads pages nobody granted.
    /// </summary>
    [Fact]
    public async Task UserContent_RefusesAUserInNoGroup()
    {
        var (controller, pages) = Create(Guid.NewGuid());
        pages.Pages["p"] = Page(PageVisibility.User);

        var result = Assert.IsType<StatusCodeResult>(await controller.UserContent("p"));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    /// <summary>
    /// Everyone's grant reaches users in no group.
    /// True: an administrator can open a page to every signed in user through Everyone.
    /// False: Everyone's pages never open.
    /// </summary>
    [Fact]
    public async Task UserContent_ServesAPageEveryoneGivesToAUserInNoGroup()
    {
        var config = new PluginConfiguration();
        config.Everyone.Permissions.CustomPages.PageIds.Add(PageId);
        var (controller, pages) = Create(Guid.NewGuid(), config: config);
        pages.Pages["p"] = Page(PageVisibility.User);

        Assert.IsType<ContentResult>(await controller.UserContent("p"));
    }

    /// <summary>
    /// An administrator opens every Signed in page without any group.
    /// True: administrators are never locked out of their own pages.
    /// False: an administrator needs a group to read a page they wrote.
    /// </summary>
    [Fact]
    public async Task UserContent_ServesAnAdministratorWithoutAGroup()
    {
        var (controller, pages) = Create(Guid.NewGuid(), admin: true);
        pages.Pages["p"] = Page(PageVisibility.User);

        Assert.IsType<ContentResult>(await controller.UserContent("p"));
    }

    /// <summary>
    /// A caller that cannot be resolved is refused.
    /// True: a bad token never reads a Signed in page.
    /// False: a revoked token keeps reading pages.
    /// </summary>
    [Fact]
    public async Task UserContent_RefusesWhenTheCallerCannotBeResolved()
    {
        var pages = new StubPageService();
        var controller = TestController.Create(pages: pages, authorization: TestController.Authorization(null, failure: new SecurityException("bad token")));
        pages.Pages["p"] = Page(PageVisibility.User);

        var result = Assert.IsType<StatusCodeResult>(await controller.UserContent("p"));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(0, pages.Renders);
    }

    /// <summary>
    /// An API key carries no user and is in no group, so it cannot open a Signed in page.
    /// True: a page private to groups stays private to them.
    /// False: any API key reads every Signed in page.
    /// </summary>
    [Fact]
    public async Task UserContent_RefusesAnApiKeyCaller()
    {
        var (controller, pages) = Create(Guid.NewGuid(), isApiKey: true, config: new PluginConfiguration { Everyone = { Permissions = { CustomPages = { AllPages = true } } } });
        pages.Pages["p"] = Page(PageVisibility.User);

        var result = Assert.IsType<StatusCodeResult>(await controller.UserContent("p"));
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(0, pages.Renders);
    }

    /// <summary>
    /// An Administrators page serves the administrator the elevation policy let in, with no group needed.
    /// True: an administrator reads every Administrators page.
    /// False: an Administrators page is closed even to administrators.
    /// </summary>
    [Fact]
    public async Task AdminContent_ServesTheAdministratorTheRouteLetIn()
    {
        var (controller, pages) = Create(Guid.NewGuid(), admin: true);
        pages.Pages["p"] = Page(PageVisibility.Admin);

        Assert.IsType<ContentResult>(await controller.AdminContent("p"));
    }

    /// <summary>
    /// The page list for the group editor gives each page's id, title, slug, and tier.
    /// True: a group can name a page by its id.
    /// False: the group editor has nothing to list.
    /// </summary>
    [Fact]
    public void ListPages_GivesEachPagesIdTitleSlugAndTier()
    {
        var config = new PluginConfiguration();
        config.CustomPages.Pages.Add(new CustomPage { Id = "a1", Title = "Rules", Slug = "rules", Visibility = PageVisibility.User });
        var controller = TestController.CreateAdmin(config: config);

        var list = controller.ListPages().Value!;

        var page = Assert.Single(list);
        Assert.Equal(("a1", "Rules", "rules", PageVisibility.User), (page.Id, page.Title, page.Slug, page.Visibility));
    }

    /// <summary>
    /// The page list is for administrators only.
    /// True: the elevation policy guards it.
    /// False: anyone could list every page and its tier.
    /// </summary>
    [Fact]
    public void ListPages_RequiresElevation()
        => Assert.Equal("RequiresElevation", typeof(PagesController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);

    // MARK: Asset endpoint

    [Theory]
    [InlineData(PageVisibility.User)]
    [InlineData(PageVisibility.Admin)]
    public void Asset_NeverServesGatedAssetsByUrl(PageVisibility visibility)
    {
        // Gated assets are embedded into their pages instead. Returning 404 rather than 403 also keeps
        // the public endpoint from disclosing which gated names exist.
        var (controller, pages) = Create();
        pages.Assets["logo.png"] = Asset(visibility);

        Assert.IsType<NotFoundResult>(controller.Asset("logo.png"));
        Assert.Equal(0, pages.AssetReads);
    }

    [Fact]
    public void Asset_ServesAnonymousAssetSandboxed()
    {
        var (controller, pages) = Create();
        var asset = Asset(PageVisibility.Anonymous);
        pages.Assets["logo.png"] = asset;
        pages.Bytes[asset] = new byte[] { 1, 2, 3 };

        var result = Assert.IsType<FileContentResult>(controller.Asset("logo.png"));

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("default-src 'none'; sandbox", controller.Response.Headers["Content-Security-Policy"]);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact]
    public void Asset_NonImageIsServedAsOpaqueDownload()
    {
        var (controller, pages) = Create();
        var asset = new PageAsset
        {
            Name = "notes.txt",
            Visibility = PageVisibility.Anonymous,
            ContentType = "text/html"
        };
        pages.Assets["notes.txt"] = asset;
        pages.Bytes[asset] = new byte[] { 1 };

        var result = Assert.IsType<FileContentResult>(controller.Asset("notes.txt"));

        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public void Asset_UndecodableAssetReturnsNotFound()
    {
        var (controller, pages) = Create();
        var asset = Asset(PageVisibility.Anonymous);
        pages.Assets["logo.png"] = asset;
        
        Assert.IsType<NotFoundResult>(controller.Asset("logo.png"));
    }

    [Fact]
    public void Favicon_ReturnsNotFoundWhenUnavailable()
    {
        // A substitute's web path is empty, so no favicon can be located.
        var (controller, _) = Create();

        Assert.IsType<NotFoundResult>(controller.Favicon());
    }

    // MARK: Authorization attributes
    //
    // The tier checks above only matter if the framework is still gating these actions, so assert the
    // attributes themselves rather than trusting that nobody deletes one during a refactor.

    [Theory]
    [InlineData(nameof(CustomPagesController.Get))]
    [InlineData(nameof(CustomPagesController.Asset))]
    [InlineData(nameof(CustomPagesController.Favicon))]
    public void PublicActions_AreExplicitlyAnonymous(string action)
        => Assert.NotNull(Method(action).GetCustomAttribute<AllowAnonymousAttribute>());

    [Fact]
    public void UserContent_RequiresAnAuthenticatedUser()
    {
        var authorize = Method(nameof(CustomPagesController.UserContent)).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Null(Method(nameof(CustomPagesController.UserContent)).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void AdminContent_RequiresElevation()
    {
        var authorize = Method(nameof(CustomPagesController.AdminContent)).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("RequiresElevation", authorize!.Policy);
    }

    private static MethodInfo Method(string name)
        => typeof(CustomPagesController).GetMethods().First(m => m.Name == name);
}
