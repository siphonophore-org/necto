using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.CustomPages;

/// <summary>
/// Finds pages and images in the settings and renders the HTML served for them from the embedded templates.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IPageService))]
public sealed partial class PageService : IPageService
{
    // The frame's sandbox for every page that has not opted out. allow-same-origin is left out, which keeps the frame
    // on an opaque origin, away from the viewer's session. The leading space lets the opt out put an empty string.
    private const string SandboxAttribute =
        " sandbox=\"allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox\"";

    // Defines page, the address the page was served at, and root, the server's base path, for the injected helpers.
    // The page is an about:srcdoc frame whose base address is the served page's. base-uri 'none' keeps it fixed.
    private const string RootLookup =
        "var page=new URL(document.baseURI),root=page.pathname.split(\"/pages/\")[0];";

    // Defines token(), a promise of this server's saved access token or null. A client signed in to several servers
    // keeps one entry each, so the entry is matched by this server's id, read from the public info route, or by an
    // address on this exact origin, and never by position. The lookup is cached on the window.
    private const string TokenLookup =
        "function token(){if(!window.__nectoPagesToken){window.__nectoPagesToken=fetch(root+\"/System/Info/Public\")"
        + ".then(function(r){return r.ok?r.json():null;}).catch(function(){return null;}).then(function(info){"
        + "function id(v){return String(v||\"\").replace(/-/g,\"\").toLowerCase();}"
        + "var want=id(info&&info.Id),here=page.origin.toLowerCase(),s=[];"
        + "try{var raw=localStorage.getItem(\"jellyfin_credentials\");if(raw){s=(JSON.parse(raw)||{}).Servers||[];}}catch(e){}"
        + "for(var i=0;i<s.length;i++){var v=s[i];if(!v||!v.AccessToken){continue;}"
        + "if(want&&id(v.Id)===want){return v.AccessToken;}"
        + "var a=[v.ManualAddress,v.LocalAddress,v.RemoteAddress];"
        + "for(var j=0;j<a.length;j++){try{if(a[j]&&new URL(String(a[j])).origin.toLowerCase()===here){return v.AccessToken;}}catch(e){}}}"
        + "return null;});}return window.__nectoPagesToken;}";

    private readonly Func<CustomPagesSettings?> _settings;

    // Decoded image bytes by image instance. A save replaces the whole settings object, so an entry goes with the
    // settings that made it, and a name reused by a later save never serves the old bytes.
    private readonly ConditionalWeakTable<PageAsset, byte[]> _assetBytes = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class.
    /// </summary>
    /// <param name="store">Reads the Custom pages settings.</param>
    public PageService(IConfigurationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _settings = () => store.Read(static c => c.CustomPages);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PageService"/> class over a settings source, for tests.
    /// </summary>
    /// <param name="settings">Gives the Custom pages settings, or <c>null</c> when there are none.</param>
    internal PageService(Func<CustomPagesSettings?> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public CustomPage? Find(string slug)
    {
        if (!IsValidSlug(slug))
        {
            return null;
        }

        return _settings()?.Pages.FirstOrDefault(p =>
            p.Enabled && string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Renders a page in a sandboxed frame on an opaque origin, so its script cannot read the viewer's token, cookies,
    /// or storage, unless the page opts out.
    /// </summary>
    /// <remarks>
    /// The author's markup only ever appears HTML encoded inside the frame's srcdoc, for every page. The sign in shell
    /// writes this output into a document on the Jellyfin origin, so live author markup there would run with the
    /// viewer's session. An unsandboxed page drops the sandbox attribute outright, since a frame with both
    /// allow-same-origin and allow-scripts can remove its own sandbox anyway.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <returns>The whole document to serve.</returns>
    public string Render(CustomPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var inner = BuildInnerDocument(page);
        if (page.ApiRoutes is { Count: > 0 })
        {
            inner = InjectServerFetch(inner, page.Slug);
        }

        // The store helper needs the Jellyfin origin and the viewer's token, which an opaque frame lacks.
        if (RunsUnsandboxed(page))
        {
            inner = InjectPageStore(inner);
        }

        var assets = _settings()?.Assets;
        if (assets is not null)
        {
            inner = InlineGatedAssets(inner, page.Visibility, assets);
        }

        return TemplateLoader.Fill("necto_pages_wrapper", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TITLE"] = WebUtility.HtmlEncode(page.Title),
            ["SANDBOX"] = RunsUnsandboxed(page) ? string.Empty : SandboxAttribute,
            ["SRCDOC"] = WebUtility.HtmlEncode(inner)
        });
    }

    /// <summary>
    /// Replaces <c>asset/{name}</c> references to images that need a signed in viewer with <c>data:</c> addresses. An
    /// image fetch carries no token, so such images travel inside the page's own gated response. Only images at or
    /// below the page's tier are embedded.
    /// </summary>
    /// <param name="document">The page's inner document.</param>
    /// <param name="pageVisibility">The page's tier.</param>
    /// <param name="assets">The images. Anyone images are left out, since they have a public address.</param>
    /// <returns>The document with each eligible reference embedded.</returns>
    public static string InlineGatedAssets(string document, PageVisibility pageVisibility, IEnumerable<PageAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);

        // A tier no enum value names would compare above every tier and unlock every image, so embed nothing.
        if (!Enum.IsDefined(pageVisibility))
        {
            return document;
        }

        var dataUris = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            if (!Enum.IsDefined(asset.Visibility)
                || !asset.Visibility.RequiresAuth()
                || !pageVisibility.Covers(asset.Visibility)
                || !IsValidAssetName(asset.Name)
                || asset.ContentType is null
                || !asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(asset.DataBase64)
                || !Base64.IsValid(asset.DataBase64))
            {
                continue;
            }

            dataUris.TryAdd(asset.Name, "data:" + asset.ContentType + ";base64," + asset.DataBase64);
        }

        if (dataUris.Count == 0)
        {
            return document;
        }

        // One pass for every name, longest first, so a substituted data: address is never scanned again and a
        // reference ends where its name ends. A full stop that ends a sentence still counts as the end.
        var names = dataUris.Keys.OrderByDescending(n => n.Length).Select(Regex.Escape);
        var pattern = "asset/(" + string.Join('|', names) + ")(?!\\.?[a-z0-9_-])";

        return Regex.Replace(
            document,
            pattern,
            match => dataUris[match.Groups[1].Value],
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
    }

    /// <inheritdoc />
    public string GetShellHtml(string slug, PageVisibility visibility, string? language)
    {
        var content = TemplateLoader.Fill("necto_pages_shell", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SLUG"] = StringUtilities.EscapeJsString(slug),
            ["TIER"] = StringUtilities.EscapeJsString(visibility.EndpointTier()),
            ["SIGNIN_HEADING"] = Text(TranslationKeys.PagesHeaderSignInRequired, language),
            ["SIGNIN_MESSAGE"] = Text(TranslationKeys.PagesMessageSignInRequired, language),
            ["SIGNIN_BUTTON"] = Text(TranslationKeys.ButtonSignIn, language),
            ["DENIED_HEADING"] = Text(TranslationKeys.PagesHeaderNotAuthorized, language),
            ["DENIED_MESSAGE"] = Text(TranslationKeys.PagesMessageNotAuthorized, language),
            ["MISSING_HEADING"] = Text(TranslationKeys.HeaderPageNotFound, language),
            ["MISSING_MESSAGE"] = Text(TranslationKeys.PagesMessagePageMissing, language),
            ["BACK_BUTTON"] = Text("Stolon.BackToJellyfin", language)
        });

        return StatusPage.Render(
            StolonTranslations.Get("Stolon.Loading", language),
            string.Empty,
            buttonText: null,
            showSpinner: true,
            content: content);
    }

    /// <inheritdoc />
    public string NotFoundHtml(string slug, string? language)
    {
        var where = IsValidSlug(slug) ? "/pages/" + slug : StolonTranslations.Get(TranslationKeys.PagesMessageThisAddress, language);
        return StatusPage.Render(
            StolonTranslations.Get(TranslationKeys.HeaderPageNotFound, language),
            StolonTranslations.Format(TranslationKeys.PagesMessageNothingPublished, language, where),
            buttonText: StolonTranslations.Get("Stolon.BackToJellyfin", language),
            buttonHref: "../web/");
    }

    /// <summary>
    /// Works out whether a slug holds only the safe address characters.
    /// </summary>
    /// <param name="slug">The slug.</param>
    /// <returns><c>true</c> when the slug is not empty and matches <c>[a-z0-9_-]+</c>.</returns>
    public static bool IsValidSlug(string? slug)
        => !string.IsNullOrEmpty(slug) && SlugPattern().IsMatch(slug);

    /// <inheritdoc />
    public PageAsset? FindAsset(string name)
    {
        if (!IsValidAssetName(name))
        {
            return null;
        }

        return _settings()?.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public byte[]? GetAssetBytes(PageAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (_assetBytes.TryGetValue(asset, out var cached))
        {
            return cached;
        }

        if (string.IsNullOrEmpty(asset.DataBase64))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(asset.DataBase64);
        }
        catch (FormatException)
        {
            return null;
        }

        _assetBytes.AddOrUpdate(asset, bytes);
        return bytes;
    }

    /// <summary>
    /// Works out whether an image name is one safe file name, with no path in it.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><c>true</c> when the name matches <c>[a-z0-9._-]+</c> and is not made only of dots.</returns>
    public static bool IsValidAssetName(string? name)
        => !string.IsNullOrEmpty(name) && name != "." && name != ".." && AssetNamePattern().IsMatch(name);

    /// <summary>
    /// Works out whether a page runs without the sandbox, because it asked for system access or has server side routes.
    /// </summary>
    /// <remarks>
    /// Routes imply it, since the injected helper reads the viewer's token and calls the route on the Jellyfin origin,
    /// and a sandboxed page with routes could only fail.
    /// </remarks>
    /// <param name="page">The page.</param>
    /// <returns><c>true</c> when the sandbox is left off.</returns>
    public static bool RunsUnsandboxed(CustomPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.Unsandboxed || page.ApiRoutes is { Count: > 0 };
    }

    /// <summary>
    /// Injects <c>serverFetch(name, init)</c>, which calls a named server side route with the viewer's token, so a
    /// page needs neither its slug nor the token. Headers may be an object, a <c>Headers</c>, or pairs, and the
    /// caller's <c>init</c> is left untouched.
    /// </summary>
    /// <param name="document">The page's inner document.</param>
    /// <param name="slug">The page's slug, built into the route base.</param>
    /// <returns>The document with the helper first.</returns>
    public static string InjectServerFetch(string document, string slug)
    {
        ArgumentNullException.ThrowIfNull(document);

        var prelude =
            "<script>(function(){" + RootLookup + "var base=root+\"/pages/\"+\"" + StringUtilities.EscapeJsString(slug) + "\"+\"/api/\";"
            + TokenLookup
            + "window.serverFetch=function(name,init){init=init||{};"
            + "return token().then(function(t){var h=new Headers(init.headers||undefined);"
            + "if(t){h.set(\"Authorization\",\"MediaBrowser Token=\\\"\"+t+\"\\\"\");}"
            + "return fetch(base+encodeURIComponent(name),Object.assign({},init,{headers:h}));});};})();</script>";

        return InsertPrelude(document, prelude);
    }

    /// <summary>
    /// Injects the <c>pageStore</c> helper into an unsandboxed page, so its script reads and writes a store without
    /// building addresses or handling the token. Every call is checked on the server against the store's own tiers.
    /// <c>write</c> takes a record id or an object with <c>id</c> and <c>label</c> as its third argument.
    /// </summary>
    /// <param name="document">The page's inner document.</param>
    /// <returns>The document with the helper first.</returns>
    public static string InjectPageStore(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var prelude =
            "<script>(function(){" + RootLookup + "var base=root+\"/pages/store/\";"
            + TokenLookup
            + "function call(url,body){return token().then(function(t){var init={method:body===undefined?\"GET\":\"POST\",headers:{}};"
            + "if(t){init.headers[\"Authorization\"]=\"MediaBrowser Token=\\\"\"+t+\"\\\"\";}"
            + "if(body!==undefined){init.headers[\"Content-Type\"]=\"application/json\";init.body=JSON.stringify(body);}"
            + "return fetch(url,init);}).then(function(r){if(!r.ok){var e=new Error(\"pageStore: \"+r.status);"
            + "e.status=r.status;throw e;}return r.status===204?null:r.json();});}"
            + "window.pageStore={"
            + "read:function(name,id){var u=base+encodeURIComponent(name)+\"/read\";"
            + "if(id)return call(u+\"?id=\"+encodeURIComponent(id));"
            + "return call(u).then(function(r){return (r&&r.records)||[];});},"
            + "readAll:function(name){return call(base+encodeURIComponent(name)+\"/read\");},"
            + "write:function(name,data,opts){var o=(typeof opts===\"string\")?{id:opts}:(opts||{});"
            + "return call(base+encodeURIComponent(name)+\"/write\","
            + "{id:o.id||null,label:o.label||null,data:data===undefined?null:data});},"
            + "remove:function(name,id){return call(base+encodeURIComponent(name)+\"/delete\",{id:id});}"
            + "};})();</script>";

        return InsertPrelude(document, prelude);
    }

    /// <summary>
    /// Puts an injected script as early as it can go, so it is defined before any author script: inside the head, else
    /// at the start of the body, else at the front.
    /// </summary>
    private static string InsertPrelude(string document, string prelude)
    {
        var headIndex = document.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            return document.Insert(headIndex + "<head>".Length, prelude);
        }

        var bodyIndex = document.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
        {
            return document.Insert(bodyIndex + "<body>".Length, prelude);
        }

        return prelude + document;
    }

    /// <summary>
    /// Gives a string for the shell's markup, encoded, since a translation is text and never markup.
    /// </summary>
    private static string Text(string key, string? language) => WebUtility.HtmlEncode(StolonTranslations.Get(key, language));

    private static string BuildInnerDocument(CustomPage page)
    {
        if (page.SingleFile)
        {
            return page.Document ?? string.Empty;
        }

        return TemplateLoader.Fill("necto_pages_inner", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TITLE"] = WebUtility.HtmlEncode(page.Title),
            ["CSS"] = page.Css ?? string.Empty,
            ["BODY"] = page.Html ?? string.Empty,
            ["JS"] = page.Js ?? string.Empty
        });
    }

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[a-z0-9._-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex AssetNamePattern();
}
