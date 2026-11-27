using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.CustomPages;

/// <summary>
/// Tests for the pure rendering and validation logic in <see cref="PageService"/>.
/// </summary>
public class PageServiceTests
{
    private static PageService CreateService()
        => new PageService(() => null);

    private static PageService CreateService(CustomPagesSettings configuration)
        => new PageService(() => configuration);

    private static CustomPagesSettings ConfigWith(params object[] items)
    {
        var config = new CustomPagesSettings();
        foreach (var item in items)
        {
            if (item is CustomPage page)
            {
                config.Pages.Add(page);
            }
            else if (item is PageAsset asset)
            {
                config.Assets.Add(asset);
            }
        }

        return config;
    }

    // MARK: Slug validation

    [Theory]
    [InlineData("abc")]
    [InlineData("a-b_c")]
    [InlineData("page1")]
    [InlineData("ABC")]
    public void IsValidSlug_AllowsSafeNames(string slug)
        => Assert.True(PageService.IsValidSlug(slug));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("../etc")]
    [InlineData("a!b")]
    public void IsValidSlug_RejectsUnsafeNames(string? slug)
        => Assert.False(PageService.IsValidSlug(slug));

    // MARK: Asset name validation

    [Theory]
    [InlineData("logo.png")]
    [InlineData("my-image_2.jpg")]
    [InlineData("a.b.c.svg")]
    public void IsValidAssetName_AllowsSafeNames(string name)
        => Assert.True(PageService.IsValidAssetName(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b.png")]
    [InlineData("a\\b.png")]
    [InlineData("a b.png")]
    public void IsValidAssetName_RejectsUnsafeNames(string? name)
        => Assert.False(PageService.IsValidAssetName(name));

    // MARK: Sandbox isolation (security invariant)

    [Fact]
    public void Render_WrapsContentInSandboxedIframeWithoutSameOrigin()
    {
        var html = CreateService().Render(new CustomPage { Title = "t", Html = "<p>hi</p>" });

        Assert.Contains("<iframe sandbox=", html, System.StringComparison.Ordinal);
        Assert.Contains("srcdoc=", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("{{SANDBOX}}", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EncodesAuthorContentIntoSrcdoc_NoTopLevelInjection()
    {
        var page = new CustomPage
        {
            Title = "<script>alert(1)</script>",
            Html = "<b>body</b>"
        };

        var html = CreateService().Render(page);

        // Neither the malicious title nor the author body appear as live top-level markup.
        Assert.DoesNotContain("<script>alert(1)</script>", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("<b>body</b>", html, System.StringComparison.Ordinal);
        // They are present only in HTML-encoded form (inside the srcdoc / title).
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, System.StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;body&lt;/b&gt;", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Unsandboxed_DropsTheSandboxAttribute()
    {
        var page = new CustomPage { Title = "t", Html = "<p>hi</p>", Unsandboxed = true };

        var html = CreateService().Render(page);

        Assert.Contains("<iframe srcdoc=", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("sandbox=", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("{{SANDBOX}}", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Unsandboxed_StillEncodesAuthorContentIntoSrcdoc()
    {
        // Dropping the sandbox must not also drop the encoding that keeps author markup out of the
        // top level document, which is what the auth shell writes this response into.
        var page = new CustomPage
        {
            Title = "<script>alert(1)</script>",
            Html = "<b>body</b>",
            Unsandboxed = true
        };

        var html = CreateService().Render(page);

        Assert.DoesNotContain("<script>alert(1)</script>", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("<b>body</b>", html, System.StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, System.StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;body&lt;/b&gt;", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_APageWithRoutesIsServedUnsandboxedWithoutAskingForSystemAccess()
    {
        // The helper cannot read the token or call the route from an opaque origin, so a sandboxed page
        // with routes could only fail silently. Defining a route is the opt in.
        var page = new CustomPage { Title = "t", Html = "<p>hi</p>", Unsandboxed = false };
        page.ApiRoutes.Add(new PageApiRoute { Name = "backend", Url = "http://x/y" });

        Assert.True(PageService.RunsUnsandboxed(page));
        Assert.DoesNotContain("sandbox=", CreateService().Render(page), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_APageWithNeitherStaysSandboxed()
    {
        var page = new CustomPage { Title = "t", Html = "<p>hi</p>" };

        Assert.False(PageService.RunsUnsandboxed(page));
        Assert.Contains("<iframe sandbox=", CreateService().Render(page), System.StringComparison.Ordinal);
    }

    // MARK: serverFetch injection

    [Fact]
    public void Render_InjectsServerFetch_WhenThePageDefinesRoutes()
    {
        var page = new CustomPage { Title = "t", Html = "<p>hi</p>" };
        page.ApiRoutes.Add(new PageApiRoute { Name = "backend", Url = "http://x/y" });

        var html = CreateService().Render(page);

        // The helper is present, HTML encoded inside the srcdoc, and carries this page's slug.
        Assert.Contains("serverFetch", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_OmitsServerFetch_WhenNoRoutesAreDefined()
    {
        var html = CreateService().Render(new CustomPage { Title = "t", Html = "<p>hi</p>" });

        Assert.DoesNotContain("serverFetch", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InjectServerFetch_LandsInsideHeadWhenPresent()
    {
        var doc = "<!DOCTYPE html><html><head><title>x</title></head><body>hi</body></html>";

        var injected = PageService.InjectServerFetch(doc, "download");

        var head = injected.IndexOf("<head>", System.StringComparison.Ordinal);
        var helper = injected.IndexOf("serverFetch", System.StringComparison.Ordinal);
        var title = injected.IndexOf("<title>", System.StringComparison.Ordinal);
        Assert.True(head < helper && helper < title);
    }

    // MARK: Composed vs single-file rendering

    [Fact]
    public void Render_ComposedMode_IncludesHtmlCssAndJs()
    {
        var page = new CustomPage
        {
            SingleFile = false,
            Html = "BODYMARK",
            Css = "CSSMARK",
            Js = "JSMARK"
        };

        var html = CreateService().Render(page);

        Assert.Contains("BODYMARK", html, System.StringComparison.Ordinal);
        Assert.Contains("CSSMARK", html, System.StringComparison.Ordinal);
        Assert.Contains("JSMARK", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SingleFileMode_UsesDocumentAndIgnoresComponents()
    {
        var page = new CustomPage
        {
            SingleFile = true,
            Document = "DOCMARK",
            Html = "BODYMARK",
            Css = "CSSMARK",
            Js = "JSMARK"
        };

        var html = CreateService().Render(page);

        Assert.Contains("DOCMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("BODYMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("CSSMARK", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("JSMARK", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_TemplateFillIsSinglePass_AuthorPlaceholdersNotReSubstituted()
    {
        // The body literally contains the {{TITLE}} token; single-pass fill must not expand it.
        var page = new CustomPage { Title = "RealTitle", Html = "{{TITLE}}" };

        var html = CreateService().Render(page);

        Assert.Contains("RealTitle", html, System.StringComparison.Ordinal);
        Assert.Contains("{{TITLE}}", html, System.StringComparison.Ordinal);
    }

    // MARK: Loading screen values

    /// <summary>
    /// The loading screen asks for the content at the page's own tier, and carries its sentences as text.
    /// True: a signed in viewer gets the content route their tier guards, and a refused one reads why.
    /// False: the screen asks the wrong route, or shows a raw token.
    /// </summary>
    [Theory]
    [InlineData(PageVisibility.User, "user")]
    [InlineData(PageVisibility.Admin, "admin")]
    public void LoaderValues_GiveTheTierAndTheSentences(PageVisibility visibility, string expected)
    {
        var values = CreateService().LoaderValues("mypage", visibility, null);

        Assert.Equal("mypage", values["SLUG"]);
        Assert.Equal(expected, values["TIER"]);
        Assert.Equal("Your account does not have access to this page.", values["DENIED_MESSAGE"]);
        Assert.Equal("Loading", values["LOADING"]);
    }

    // MARK: Content style

    /// <summary>
    /// The content style goes first in the page's head, before the author's own CSS.
    /// True: a page with no CSS reads in the web client's look, and the author's CSS still wins.
    /// False: the style overrides the author, or never reaches the page.
    /// </summary>
    [Fact]
    public void Render_PutsContentStyleBeforeAuthorCss()
    {
        var page = new CustomPage { Title = "t", Html = "<p>hi</p>", Css = "p{color:red}" };

        var html = CreateService().Render(page, "<style id=\"web\">p{color:white}</style>");

        var web = html.IndexOf("id=&quot;web&quot;", System.StringComparison.Ordinal);
        var author = html.IndexOf("p{color:red}", System.StringComparison.Ordinal);
        Assert.True(web > 0, "the content style is in the frame");
        Assert.True(web < author, "the content style comes before the author's CSS");
    }

    /// <summary>
    /// The frame's page carries base-uri 'none' and object-src 'none' in its own policy.
    /// True: the policy holds after the loading screen writes the page into its document.
    /// False: a signed in page runs with a looser policy than an Anyone page.
    /// </summary>
    [Fact]
    public void Render_CarriesItsOwnBaseAndObjectPolicy()
    {
        var html = CreateService().Render(new CustomPage { Title = "t", Html = "<p>hi</p>" });

        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"object-src 'none'; base-uri 'none'\" />", html, System.StringComparison.Ordinal);
    }

    // MARK: Gated asset inlining

    private static PageAsset MakeAsset(
        string name,
        PageVisibility visibility,
        string contentType = "image/png",
        string? dataBase64 = null)
        => new PageAsset
        {
            Name = name,
            Visibility = visibility,
            ContentType = contentType,
            DataBase64 = dataBase64 ?? System.Convert.ToBase64String(new byte[] { 1, 2, 3 })
        };

    [Theory]
    [InlineData(PageVisibility.User, PageVisibility.User)]
    [InlineData(PageVisibility.Admin, PageVisibility.User)]
    [InlineData(PageVisibility.Admin, PageVisibility.Admin)]
    public void InlineGatedAssets_EmbedsAssetCoveredByPageTier(PageVisibility pageTier, PageVisibility assetTier)
    {
        var asset = MakeAsset("secret.png", assetTier);
        var html = PageService.InlineGatedAssets("<img src=\"asset/secret.png\">", pageTier, new[] { asset });

        Assert.DoesNotContain("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64," + asset.DataBase64, html, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PageVisibility.Anonymous, PageVisibility.User)]
    [InlineData(PageVisibility.Anonymous, PageVisibility.Admin)]
    [InlineData(PageVisibility.User, PageVisibility.Admin)]
    public void InlineGatedAssets_NeverEmbedsAssetAbovePageTier(PageVisibility pageTier, PageVisibility assetTier)
    {
        var asset = MakeAsset("secret.png", assetTier);
        var html = PageService.InlineGatedAssets("<img src=\"asset/secret.png\">", pageTier, new[] { asset });

        Assert.Contains("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_IgnoresAnonymousAssets()
    {
        // Anonymous assets stay URL served so the browser can cache them.
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);
        var html = PageService.InlineGatedAssets("<img src=\"asset/logo.png\">", PageVisibility.Admin, new[] { asset });

        Assert.Contains("asset/logo.png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_SkipsNonImageContentTypes()
    {
        var asset = MakeAsset("page.html", PageVisibility.User, contentType: "text/html");
        var html = PageService.InlineGatedAssets("<a href=\"asset/page.html\">x</a>", PageVisibility.User, new[] { asset });

        Assert.Contains("asset/page.html", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:text/html", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_SkipsInvalidBase64()
    {
        var asset = MakeAsset("bad.png", PageVisibility.User, dataBase64: "not base64!");
        var html = PageService.InlineGatedAssets("<img src=\"asset/bad.png\">", PageVisibility.User, new[] { asset });

        Assert.Contains("asset/bad.png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_MatchesReferencesCaseInsensitively()
    {
        var asset = MakeAsset("secret.png", PageVisibility.User);
        var html = PageService.InlineGatedAssets("<img src=\"Asset/Secret.PNG\">", PageVisibility.User, new[] { asset });

        Assert.DoesNotContain("Asset/Secret.PNG", html, System.StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_DoesNotRewriteTheFrontOfALongerName()
    {
        // "logo" is a prefix of "logo.png". Matching the bare prefix used to splice the shorter asset's
        // data into the longer reference, leaving "data:...;base64,AQID.png" behind.
        var shortAsset = MakeAsset("logo", PageVisibility.User, dataBase64: System.Convert.ToBase64String(new byte[] { 9, 9, 9 }));
        var longAsset = MakeAsset("logo.png", PageVisibility.User);

        var html = PageService.InlineGatedAssets(
            "<img src=\"asset/logo.png\"><img src=\"asset/logo\">", PageVisibility.User, new[] { shortAsset, longAsset });

        Assert.Equal(
            "<img src=\"data:image/png;base64," + longAsset.DataBase64 + "\"><img src=\"data:image/png;base64," + shortAsset.DataBase64 + "\">",
            html);
    }

    [Fact]
    public void InlineGatedAssets_LeavesAReferenceToAnUnknownLongerNameAlone()
    {
        var asset = MakeAsset("logo", PageVisibility.User);

        var html = PageService.InlineGatedAssets("<img src=\"asset/logo-dark.png\">", PageVisibility.User, new[] { asset });

        Assert.Equal("<img src=\"asset/logo-dark.png\">", html);
    }

    [Theory]
    [InlineData("url(asset/logo.png)")]
    [InlineData("see asset/logo.png.")]
    [InlineData("asset/logo.png?v=2")]
    [InlineData("'asset/logo.png'")]
    public void InlineGatedAssets_StillMatchesAReferenceFollowedByPunctuation(string reference)
    {
        var asset = MakeAsset("logo.png", PageVisibility.User);

        var html = PageService.InlineGatedAssets(reference, PageVisibility.User, new[] { asset });

        Assert.DoesNotContain("asset/logo.png", html, System.StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64," + asset.DataBase64, html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_NeverRescansAnEmbeddedDataUri()
    {
        // Base64 can contain "asset/", so a substituted data: URI must not be rewritten by a later asset.
        var payload = System.Convert.FromBase64String("asset/x+");
        var first = MakeAsset("a.png", PageVisibility.User, dataBase64: System.Convert.ToBase64String(payload));
        var second = MakeAsset("x", PageVisibility.User, dataBase64: System.Convert.ToBase64String(new byte[] { 7 }));

        var html = PageService.InlineGatedAssets("asset/a.png", PageVisibility.User, new[] { first, second });

        Assert.Equal("data:image/png;base64,asset/x+", html);
    }

    // MARK: Asset byte decoding

    [Fact]
    public void GetAssetBytes_DecodesValidBase64()
    {
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);

        var bytes = CreateService().GetAssetBytes(asset);

        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public void GetAssetBytes_ReturnsNullForInvalidBase64()
    {
        var asset = MakeAsset("bad.png", PageVisibility.Anonymous, dataBase64: "not base64!");

        Assert.Null(CreateService().GetAssetBytes(asset));
    }

    [Fact]
    public void GetAssetBytes_ReturnsCachedBytesForRepeatedRequests()
    {
        var service = CreateService();
        var asset = MakeAsset("logo.png", PageVisibility.Anonymous);

        var first = service.GetAssetBytes(asset);
        var second = service.GetAssetBytes(asset);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // MARK: Visibility tier ordering

    [Theory]
    [InlineData(PageVisibility.Anonymous, PageVisibility.Anonymous, true)]
    [InlineData(PageVisibility.User, PageVisibility.Anonymous, true)]
    [InlineData(PageVisibility.Admin, PageVisibility.User, true)]
    [InlineData(PageVisibility.Anonymous, PageVisibility.User, false)]
    [InlineData(PageVisibility.User, PageVisibility.Admin, false)]
    public void Covers_FollowsTierOrdering(PageVisibility tier, PageVisibility required, bool expected)
        => Assert.Equal(expected, tier.Covers(required));

    // MARK: Resolution against configuration

    [Fact]
    public void Find_ResolvesEnabledPageCaseInsensitively()
    {
        var service = CreateService(ConfigWith(new CustomPage { Slug = "alpha", Enabled = true }));

        Assert.NotNull(service.Find("ALPHA"));
    }

    [Fact]
    public void Find_IgnoresDisabledPages()
    {
        // Unpublishing a page is what makes /pages/{slug} return 404.
        var service = CreateService(ConfigWith(new CustomPage { Slug = "alpha", Enabled = false }));

        Assert.Null(service.Find("alpha"));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Find_RejectsUnsafeSlugsWithoutConsultingConfiguration(string slug)
    {
        var service = CreateService(ConfigWith(new CustomPage { Slug = "alpha", Enabled = true }));

        Assert.Null(service.Find(slug));
    }

    [Fact]
    public void Find_ReturnsNullWhenConfigurationUnavailable()
        => Assert.Null(new PageService(() => null).Find("alpha"));

    [Fact]
    public void FindAsset_ResolvesByNameCaseInsensitively()
    {
        var service = CreateService(ConfigWith(MakeAsset("logo.png", PageVisibility.Anonymous)));

        Assert.NotNull(service.FindAsset("LOGO.PNG"));
    }

    [Fact]
    public void Render_EmbedsGatedAssetsFromConfiguration()
    {
        var asset = MakeAsset("secret.png", PageVisibility.User);
        var page = new CustomPage
        {
            Visibility = PageVisibility.User,
            Html = "<img src=\"asset/secret.png\">"
        };

        var html = CreateService(ConfigWith(page, asset)).Render(page);

        Assert.Contains("data:image/png;base64," + asset.DataBase64, html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("asset/secret.png", html, System.StringComparison.Ordinal);
    }

    // MARK: Out-of-range tiers
    //
    // Save-time validation rejects these, but a hand-edited XML config never passes through it, and an
    // undefined value compares as covering every tier.

    [Fact]
    public void InlineGatedAssets_EmbedsNothingForUndefinedPageVisibility()
    {
        var asset = MakeAsset("secret.png", PageVisibility.Admin);
        var html = PageService.InlineGatedAssets(
            "<img src=\"asset/secret.png\">", (PageVisibility)7, new[] { asset });

        Assert.Contains("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InlineGatedAssets_SkipsAssetWithUndefinedVisibility()
    {
        var asset = MakeAsset("secret.png", (PageVisibility)7);
        var html = PageService.InlineGatedAssets(
            "<img src=\"asset/secret.png\">", PageVisibility.Admin, new[] { asset });

        Assert.Contains("asset/secret.png", html, System.StringComparison.Ordinal);
        Assert.DoesNotContain("data:image/png", html, System.StringComparison.Ordinal);
    }

    // MARK: Asset byte cache identity

    [Fact]
    public void GetAssetBytes_KeepsAssetsWithTheSameNameDistinct()
    {
        // The cache is keyed by the asset instance, not its name, so an asset resolved just before a
        // configuration save can never publish its bytes under a name the new configuration reused.
        var service = CreateService();
        var before = MakeAsset("logo.png", PageVisibility.Anonymous, dataBase64: System.Convert.ToBase64String(new byte[] { 1 }));
        var after = MakeAsset("logo.png", PageVisibility.Anonymous, dataBase64: System.Convert.ToBase64String(new byte[] { 2 }));

        Assert.Equal(new byte[] { 1 }, service.GetAssetBytes(before));
        Assert.Equal(new byte[] { 2 }, service.GetAssetBytes(after));
        Assert.Equal(new byte[] { 1 }, service.GetAssetBytes(before));
    }

    [Fact]
    public void GetAssetBytes_ReturnsNullForEmptyData()
    {
        var asset = MakeAsset("empty.png", PageVisibility.Anonymous, dataBase64: string.Empty);

        Assert.Null(CreateService().GetAssetBytes(asset));
    }
    // MARK: pageStore injection

    [Fact]
    public void Render_SandboxedPage_DoesNotGetTheStoreHelper()
    {
        // The helper needs the Jellyfin origin and the viewer's token, and an opaque origin frame has
        // neither, so a sandboxed page must not be handed a helper that could only fail.
        var page = new CustomPage { SingleFile = true, Document = "<html><head></head><body></body></html>" };

        var html = CreateService().Render(page);

        Assert.DoesNotContain("pageStore", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_UnsandboxedPage_GetsTheStoreHelper()
    {
        var page = new CustomPage
        {
            SingleFile = true,
            Unsandboxed = true,
            Document = "<html><head></head><body></body></html>"
        };

        var html = CreateService().Render(page);

        Assert.Contains("window.pageStore", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PageWithRoutes_GetsBothHelpers()
    {
        // Routes already imply the page runs unsandboxed, so the store helper rides along with them.
        var page = new CustomPage { SingleFile = true, Document = "<html><head></head><body></body></html>" };
        page.ApiRoutes.Add(new PageApiRoute { Name = "grabber", Url = "http://localhost:1/" });

        var html = CreateService().Render(page);

        Assert.Contains("window.serverFetch", html, System.StringComparison.Ordinal);
        Assert.Contains("window.pageStore", html, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InjectPageStore_LandsInsideHeadWhenPresent()
    {
        var doc = "<html><head></head><body><script>pageStore.read('x');</script></body></html>";

        var injected = PageService.InjectPageStore(doc);

        var helper = injected.IndexOf("window.pageStore", System.StringComparison.Ordinal);
        var author = injected.IndexOf("pageStore.read('x')", System.StringComparison.Ordinal);
        Assert.True(helper >= 0 && helper < author);
    }

    [Fact]
    public void InjectPageStore_DocumentWithoutAHead_StillGetsTheHelper()
    {
        var injected = PageService.InjectPageStore("<p>fragment</p>");

        Assert.Contains("window.pageStore", injected, System.StringComparison.Ordinal);
        Assert.Contains("<p>fragment</p>", injected, System.StringComparison.Ordinal);
    }

    // MARK: Which saved credential the helpers send

    public static TheoryData<string> InjectedHelpers => new()
    {
        PageService.InjectServerFetch("<p>x</p>", "download"),
        PageService.InjectPageStore("<p>x</p>")
    };

    [Theory]
    [MemberData(nameof(InjectedHelpers))]
    public void InjectedHelpers_PickThisServersTokenByIdOrOrigin(string injected)
    {
        // A client signed in to several servers keeps one entry each. The helpers used to send whichever
        // entry came first, which could hand this server another server's token.
        Assert.Contains("/System/Info/Public", injected, System.StringComparison.Ordinal);
        Assert.Contains("id(v.Id)===want", injected, System.StringComparison.Ordinal);
        Assert.Contains(".origin.toLowerCase()===here", injected, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s[i].AccessToken", injected, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InjectedHelpers))]
    public void InjectedHelpers_HonourTheServerBasePath(string injected)
    {
        // Behind a base URL the server answers at /base/System/Info/Public and /base/pages/..., so the
        // helpers derive the base path from the served page's address, as the shell does, instead of
        // calling the origin root.
        Assert.Contains("new URL(document.baseURI)", injected, System.StringComparison.Ordinal);
        Assert.Contains("fetch(root+\"/System/Info/Public\")", injected, System.StringComparison.Ordinal);
        Assert.Contains("var base=root+\"/pages/", injected, System.StringComparison.Ordinal);
        Assert.DoesNotContain("location.origin", injected, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InjectedHelpers))]
    public void InjectedHelpers_SendNoTokenWhenNoneBelongsToThisServer(string injected)
    {
        // The Authorization header is only set once a matching token was found.
        Assert.Contains("if(t){", injected, System.StringComparison.Ordinal);
        Assert.Contains("return null;", injected, System.StringComparison.Ordinal);
    }
    // MARK: serverFetch request headers

    [Theory]
    [InlineData("{ 'X-Csrf': 'abc' }")]
    [InlineData("new Headers({ 'X-Csrf': 'abc' })")]
    [InlineData("[['X-Csrf', 'abc']]")]
    public void ServerFetch_SendsTheTokenWithEveryHeadersShape(string headers)
    {
        var sent = RunServerFetch(headers);

        Assert.Equal("MediaBrowser Token=\"tok\"|abc|untouched", sent);
    }

    // Runs the injected helper under node with a stub page, a saved token for this origin, and a fetch
    // that reports the Authorization and X-Csrf headers it received and whether the caller's init kept
    // its own headers.
    private static string RunServerFetch(string headers)
    {
        var prelude = PageService.InjectServerFetch("<p>x</p>", "demo");
        var script = prelude.Substring("<script>".Length, prelude.IndexOf("</script>", System.StringComparison.Ordinal) - "<script>".Length);
        var harness =
            "globalThis.window=globalThis;"
            + "globalThis.document={baseURI:'http://jf.test/pages/demo'};"
            + "globalThis.localStorage={getItem:()=>JSON.stringify({Servers:[{AccessToken:'tok',ManualAddress:'http://jf.test'}]})};"
            + "globalThis.fetch=(url,init)=>url.endsWith('/System/Info/Public')"
            + "?Promise.resolve({ok:false}):Promise.resolve(new Headers(init.headers));"
            + script
            + "const init={headers:" + headers + "};"
            + "serverFetch('route',init).then(h=>{const own=new Headers(init.headers);"
            + "process.stdout.write(h.get('Authorization')+'|'+h.get('X-Csrf')+'|'+(own.has('Authorization')?'changed':'untouched'));});";

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "serverfetch-" + System.Guid.NewGuid().ToString("N") + ".mjs");
        System.IO.File.WriteAllText(path, harness);
        try
        {
            using var node = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            var output = node.StandardOutput.ReadToEnd();
            var error = node.StandardError.ReadToEnd();
            node.WaitForExit();
            Assert.True(node.ExitCode == 0, error);
            return output;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
