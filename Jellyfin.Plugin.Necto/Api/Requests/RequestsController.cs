using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Api.Requests;

/// <summary>
/// Serves the request page at /requests/, the issue page at /issues/ (the same page and assets, which it opens as the
/// issue page from its address), the JSON endpoints they call as the signed-in user, and the administrator endpoints
/// the dashboard uses. The page itself is served anonymously and signs the viewer
/// in client-side; every endpoint it calls is authenticated by Jellyfin.
/// </summary>
[ApiController]
[Route("requests")]
[Route("issues")]
public partial class RequestsController : ControllerBase, IActionFilter
{
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string AdminPolicy = "RequiresElevation";

    // The web client's stylesheets and logo, worked out from its folder once rather than on every anonymous hit.
    // Nothing found is kept too, but only for a few minutes, so a folder that was briefly unreadable recovers.
    private static readonly WebAssetCache<string> _fontsCss = new(css => css is { Length: > 0 });
    private static readonly WebAssetCache<string> _webClientCss = new(css => css is { Length: > 0 });
    private static readonly WebAssetCache<string> _logoPath = new(path => path is not null);
    private static readonly WebAssetCache<string> _detailsTemplate = new(html => html is { Length: > 0 });

    // The page as last built, with the fingerprint of the stylesheets it was built for. A class, so it is swapped
    // whole and a request reading it while another replaces it never sees half of each.
    private static BuiltPage? _page;
    private static string? _webClientScript;
    private static PackedAsset? _webClientAsset;

    // The assets' fingerprint as last worked out, with what it was worked out from.
    private static readonly AssetFingerprint _fingerprint = new();

    private readonly RequestService _requests;
    private readonly RequestDatabase _db;
    private readonly SearchService _search;
    private readonly DetailsService _details;
    private readonly ArrClient _arr;
    private readonly RequestNotifier _notifier;
    private readonly IServerApplicationPaths _paths;
    private readonly RequestItems _items;
    private readonly ArtworkCache _artwork;
    private readonly IServerApplicationHost? _host;
    private readonly IssueService? _issues;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsController"/> class.
    /// </summary>
    /// <param name="requests">The request service.</param>
    /// <param name="db">The request database.</param>
    /// <param name="search">The search service.</param>
    /// <param name="details">The cast, crew and people service.</param>
    /// <param name="arr">The Radarr and Sonarr client.</param>
    /// <param name="notifier">The notifier.</param>
    /// <param name="paths">The server paths, used to find the web client's favicon.</param>
    /// <param name="items">Turns requests into Jellyfin's item format.</param>
    /// <param name="artwork">TMDb's artwork, kept on the server.</param>
    /// <param name="host">The server, for its id, which the page is served with.</param>
    /// <param name="issues">The issue service, behind the issue page and the Issues tab.</param>
    public RequestsController(RequestService requests, RequestDatabase db, SearchService search, DetailsService details, ArrClient arr, RequestNotifier notifier, IServerApplicationPaths paths, RequestItems items, ArtworkCache artwork, IServerApplicationHost? host = null, IssueService? issues = null)
    {
        _host = host;
        _issues = issues;
        _artwork = artwork;
        _items = items;
        _requests = requests;
        _db = db;
        _search = search;
        _details = details;
        _arr = arr;
        _notifier = notifier;
        _paths = paths;
    }

    // ------------------------------------------------------------------ page

    /// <summary>Serves the request page: a whole document of its own, which signs the viewer in client-side with their Jellyfin session.</summary>
    /// <returns>The HTML page, or a redirect to the address with a trailing slash.</returns>
    [HttpGet]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Page()
    {
        // The page links its favicon and assets relatively, which only resolves under /requests/.
        if (Request.Path.Value?.EndsWith('/') != true)
        {
            return Redirect(Request.PathBase + Request.Path + "/" + Request.QueryString);
        }

        // The page runs on Jellyfin's origin and holds the viewer's token, so only its own scripts run (each tag carrying
        // this response's nonce, the ones loading webclient.js and page.js too, so no other script on the server can
        // be loaded into it), it calls only this server, and no other site can frame it. Images come from the server
        // and the artwork hosts; styles may come from wherever the server's custom CSS imports them.
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'nonce-" + nonce + "'; style-src 'self' 'unsafe-inline' https:; "
            + "img-src 'self' https: data: blob:; font-src 'self' https: data:; connect-src 'self'; media-src 'self'; object-src 'none'; "
            + "worker-src 'self'; manifest-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        // The page holds no one's data and no token in its address (the token is read from the web client's own
        // storage), so instead of never being kept (no-store), it's kept by the browser alone and checked with the
        // server each time it's opened: going back to it from a title in the web client then restores it as it was
        // left, rather than loading it again.
        Response.Headers["Cache-Control"] = "no-cache, private";
        var html = IsIssuesPath ? AsIssuesPage(CurrentPage().Html) : CurrentPage().Html;
        return Content(
            html.Replace("<script>", "<script nonce=\"" + nonce + "\">", StringComparison.Ordinal)
                .Replace("<script src=", "<script nonce=\"" + nonce + "\" src=", StringComparison.Ordinal),
            "text/html");
    }

    // Whether the call came in under /issues/, where the same page and assets serve the issue page.
    private bool IsIssuesPath => Request.Path.StartsWithSegments("/issues", StringComparison.OrdinalIgnoreCase);

    /// <summary>Names the page as the issue page, for the browser's tab and an installed app: the rest is the same page.</summary>
    /// <param name="html">The request page as built.</param>
    /// <returns>The issue page.</returns>
    internal static string AsIssuesPage(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return html.Replace("<title>Requests</title>", "<title>Issues</title>", StringComparison.Ordinal)
            .Replace("<meta name=\"apple-mobile-web-app-title\" content=\"Requests\" />", "<meta name=\"apple-mobile-web-app-title\" content=\"Issues\" />", StringComparison.Ordinal);
    }

    /// <summary>Names the installed app the issue page, whose address the manifest's relative start and scope already follow.</summary>
    /// <param name="manifest">The request page's manifest.</param>
    /// <returns>The issue page's manifest.</returns>
    internal static string AsIssuesManifest(string manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Replace("\"name\": \"Jellyfin Requests\"", "\"name\": \"Jellyfin Issues\"", StringComparison.Ordinal)
            .Replace("\"short_name\": \"Requests\"", "\"short_name\": \"Issues\"", StringComparison.Ordinal)
            .Replace("\"description\": \"Request movies and shows for Jellyfin\"", "\"description\": \"Report problems with what Jellyfin plays\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Serves the page's own script, split out of the page as it's built so it's kept by the browser between visits
    /// (at an address that changes with every version) rather than sent again inside the page each time.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("page.js")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult PageScript()
    {
        CacheAsset(TimeSpan.FromHours(1));
        return Packed(CurrentPage().Script, "text/javascript; charset=utf-8");
    }

    /// <summary>
    /// Serves the page's own stylesheet, split out of the page as it's built, as its script is, so it's kept by the
    /// browser between visits rather than sent again inside the page each time.
    /// </summary>
    /// <returns>The stylesheet.</returns>
    [HttpGet("page.css")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult PageStyles()
    {
        CacheAsset(TimeSpan.FromHours(1));
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Packed(CurrentPage().Style, "text/css; charset=utf-8");
    }

    // The page as built for the web client installed, this version of the plugin and this server: built again when
    // any of them changes. The assets' fingerprint gives them a new address whenever what they hold changes.
    private BuiltPage CurrentPage()
    {
        var template = WebDetailsTemplate();
        var version = CurrentVersion();
        var serverId = ServerId;
        var page = _page;
        if (page is null || page.Version != version || page.Template != template || page.ServerId != serverId)
        {
            var (withoutScript, script) = SplitScript(BuildPage(EmbeddedResources.Read("necto_requests_page.html"), version, template, WebClientCss()), version);
            var (html, style) = SplitStyle(withoutScript, version);
            page = new BuiltPage(version, FillServerId(html, serverId), template, serverId, PackedAsset.Of(script), PackedAsset.Of(style));
            _page = page;
        }

        return page;
    }

    // The assets' fingerprint, worked out again only when something it's made from has changed.
    private string CurrentVersion() => _fingerprint.Of(FontsCss(), WebClientCss(), WebClientScript, PluginBuild);

    // This server's id, which never changes while it runs; empty when it isn't known.
    private string ServerId => _host?.SystemId ?? string.Empty;

    // Where the page takes this server's id.
    internal const string ServerIdSlot = "REQUESTS_SERVER_ID";

    // The page with this server's id filled in, so it knows which of the web client's stored sign-ins is for this
    // server without first asking the server who it is. Only in the page's markup, not its script (split out before
    // this), which can then still tell a page served without one. Without an id it's left as it is, and the page asks.
    internal static string FillServerId(string html, string? serverId)
    {
        ArgumentNullException.ThrowIfNull(html);
        return string.IsNullOrEmpty(serverId) ? html : html.Replace(ServerIdSlot, WebUtility.HtmlEncode(serverId), StringComparison.Ordinal);
    }

    // The page's own stylesheet, the largest <style> in its head, taken out and linked in its place (page.css). In its
    // place, so the stylesheets after it (the server's custom CSS) still come after it and win. Other style blocks, and
    // anything in the body, are left where they are.
    internal static (string Html, string Style) SplitStyle(string html, string version)
    {
        ArgumentNullException.ThrowIfNull(html);
        const string Open = "<style>";
        const string Close = "</style>";
        var head = html.IndexOf("</head>", StringComparison.Ordinal);
        if (head < 0)
        {
            return (html, string.Empty);
        }

        int start = -1, end = -1;
        for (var at = html.IndexOf(Open, 0, head, StringComparison.Ordinal); at >= 0;)
        {
            var close = html.IndexOf(Close, at, head - at, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            if (close - at > end - start)
            {
                start = at;
                end = close;
            }

            var next = close + Close.Length;
            at = next < head ? html.IndexOf(Open, next, head - next, StringComparison.Ordinal) : -1;
        }

        if (start < 0)
        {
            return (html, string.Empty);
        }

        var style = html[(start + Open.Length)..end];
        return (html[..start] + "<link rel=\"stylesheet\" href=\"page.css?v=" + version + "\" />" + html[(end + Close.Length)..], style);
    }

    // The page's own inline script, the last on the page, taken out and linked in its place (page.js).
    internal static (string Html, string Script) SplitScript(string html, string version)
    {
        ArgumentNullException.ThrowIfNull(html);
        var start = html.LastIndexOf("<script>", StringComparison.Ordinal);
        var end = start < 0 ? -1 : html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            return (html, string.Empty);
        }

        var script = html[(start + "<script>".Length)..end];
        return (html[..start] + "<script src=\"page.js?v=" + version + "\"></script>" + html[(end + "</script>".Length)..], script);
    }

    private string FontsCss() => _fontsCss.Get(() => FindFonts(_paths.WebPath)) ?? string.Empty;

    // The web client's details page template, cleaned, when it has everything the page fills in; empty otherwise.
    private string WebDetailsTemplate() => _detailsTemplate.Get(() => WebClientDetailsTemplate.Find(_paths.WebPath) is { } t && WebClientDetailsTemplate.Missing(t).Count == 0 ? t : null) ?? string.Empty;

    // The page as served: its assets' fingerprint filled in, and the web client's details page template inlined in a
    // <template> (inert: nothing in it runs or loads until the page builds its details page from it). Inlined rather
    // than fetched, so the details page is built from it at once, with no request to wait on or to fail, and is cached
    // with the page. Without one the page keeps its own copy of the details page.
    internal static string BuildPage(string html, string version, string? template, string? webClientCss = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        // The web client's stylesheets linked from the page itself, in their order, rather than imported by
        // webclient.css: the browser then fetches them all at once, as soon as it reads the page.
        if (!string.IsNullOrEmpty(webClientCss))
        {
            var links = string.Join("\n", ImportedStyle().Matches(webClientCss).Select(m => "<link rel=\"stylesheet\" href=\"" + WebUtility.HtmlEncode(m.Groups[1].Value) + "\" />"));
            if (links.Length > 0)
            {
                html = html.Replace(WebClientCssLink, links, StringComparison.Ordinal);
            }
        }

        html = html.Replace("ASSET_VERSION", version, StringComparison.Ordinal)
            .Replace("PLUGIN_VERSION", PluginVersion, StringComparison.Ordinal);
        return string.IsNullOrEmpty(template)
            ? html
            : html.Replace(DetailsTemplateSlot, "<template id=\"webDetailsTemplate\">" + template + "</template>", StringComparison.Ordinal);
    }

    // The page's link to webclient.css, which the stylesheets it imports are linked in place of.
    internal const string WebClientCssLink = "<link rel=\"stylesheet\" href=\"webclient.css?v=ASSET_VERSION\" />";

    [GeneratedRegex(@"@import url\(""([^""]+)""\);")]
    private static partial Regex ImportedStyle();

    // Where the page takes the web client's details page template.
    internal const string DetailsTemplateSlot = "<!-- WEB_DETAILS_TEMPLATE -->";

    private string WebClientCss() => _webClientCss.Get(() => FindWebClientStyles(_paths.WebPath)) ?? string.Empty;

    // The page's copy of the web client's markup, read once: it only changes with the plugin.
    private static string WebClientScript => _webClientScript ??= EmbeddedResources.Read("necto_requests_webclient.js");

    // This build of the plugin, so what it serves the page, such as its strings, gets a new address with each update.
    private static readonly string PluginBuild = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString();

    internal static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    // A short fingerprint of what the page links: the stylesheets, its script, and the plugin's build.
    internal static string AssetVersion(params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', parts));
        return Convert.ToHexString(SHA256.HashData(bytes), 0, 6).ToLowerInvariant();
    }

    /// <summary>
    /// Serves the page's script that builds the web client's markup (requests_webclient.js), cached as its stylesheets
    /// are: the page links it with the fingerprint that changes whenever it does.
    /// </summary>
    /// <returns>The script.</returns>
    [HttpGet("webclient.js")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WebClientMarkup()
    {
        CacheAsset(TimeSpan.FromHours(1));
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Packed(_webClientAsset ??= PackedAsset.Of(WebClientScript), "text/javascript; charset=utf-8");
    }

    // A script or stylesheet as it was packed once: gzipped for a browser that takes it, unless something else is
    // already encoding the answer, otherwise as it is. Either way, caches are told the answer depends on what the
    // browser takes.
    private FileContentResult Packed(PackedAsset asset, string contentType)
    {
        Response.Headers.Vary = "Accept-Encoding";
        if (TakesGzip(Request) && !Response.Headers.ContainsKey("Content-Encoding"))
        {
            Response.Headers["Content-Encoding"] = "gzip";
            return File(asset.Gzipped, contentType);
        }

        return File(asset.Bytes, contentType);
    }

    // Whether the browser takes gzip: named (or * named) in Accept-Encoding, and not with a quality of 0.
    internal static bool TakesGzip(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Microsoft.Net.Http.Headers.StringWithQualityHeaderValue.TryParseList(request.Headers.AcceptEncoding.OfType<string>().ToList(), out var taken)
            && taken.Any(e => (e.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase) || e.Value.Equals("*", StringComparison.Ordinal)) && e.Quality is not 0);
    }

    /// <summary>Serves the web client's favicon for the page.</summary>
    /// <returns>The icon, or not found.</returns>
    [HttpGet("favicon.ico")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Favicon() => PublicPage.Favicon(_paths, Response);

    /// <summary>
    /// Serves the web client's own Noto Sans faces for the page: its @font-face rules, pointed at its font files.
    /// The files carry a build hash in their names, so they are looked up in the installed web client.
    /// </summary>
    /// <returns>The stylesheet, empty when the web client has none.</returns>
    [HttpGet("fonts.css")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Fonts()
    {
        CacheAsset(TimeSpan.FromDays(1));
        return Content(FontsCss(), "text/css");
    }

    /// <summary>
    /// Serves the web client's own stylesheets as one: an @import of each one its index.html links, so what's styled by
    /// it uses the very rules the web client does, whatever version is installed. The page links them itself
    /// (BuildPage); this stays for the preview, and for a page built while none were found.
    /// </summary>
    /// <returns>The stylesheet, empty when the web client has none.</returns>
    [HttpGet("webclient.css")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/css")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WebClientStyles()
    {
        CacheAsset(TimeSpan.FromHours(1));
        return Content(WebClientCss(), "text/css");
    }

    // The stylesheets index.html links, in its order, then the ones the web client loads for the screens the page
    // stands in for (search, item details, item lists), each made relative to /requests/. Only plain relative paths
    // within the web client are taken.
    internal static string FindWebClientStyles(string? webPath)
    {
        try
        {
            var index = string.IsNullOrEmpty(webPath) ? null : Path.Combine(webPath, "index.html");
            if (index is null || !System.IO.File.Exists(index))
            {
                return string.Empty;
            }

            var html = System.IO.File.ReadAllText(index);
            var links = LinkTag().Matches(html)
                .Select(m => m.Value)
                .Where(tag => tag.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
                .Select(tag => StylesheetHref().Match(tag).Groups[1].Value)
                .Where(href => href.Length > 0 && !href.Contains("//", StringComparison.Ordinal) && !href.StartsWith('/') && !href.Contains("..", StringComparison.Ordinal)
                    && href.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '/' or '?' or '='));
            var routes = RouteStyles.Select(name => Directory.EnumerateFiles(webPath!, name + ".*.css")
                    .Select(Path.GetFileName)
                    .Where(file => file is not null && RouteStylesheet().IsMatch(file))
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .FirstOrDefault())
                .OfType<string>();

            // The site stylesheet (the base font size, headings, the phone and TV scaling) is a numbered chunk, so it
            // is found by what it holds: the root font size and the phone layout's. Other chunks set the root size for
            // their own screens (the book player's forces 82%), but only the site's scales the phone layout too.
            // The newer app layout's own stylesheet, another numbered chunk, is found by the phone details banner it lays
            // out (pulled up under the bar, with its fade): the layout the page's bar and details copy. Both are looked for in
            // one pass, reading each chunk once, and the pass stops once both are found.
            string? site = null;
            string? layout = null;
            foreach (var file in Directory.EnumerateFiles(webPath!, "*.css")
                .Where(file => ChunkStylesheet().IsMatch(Path.GetFileName(file)))
                .OrderBy(file => file, StringComparer.Ordinal))
            {
                var css = System.IO.File.ReadAllText(file);
                if (site is null && css.Contains("html{font-size:", StringComparison.Ordinal) && css.Contains(".layout-mobile{font-size:", StringComparison.Ordinal))
                {
                    site = Path.GetFileName(file);
                }

                if (layout is null && css.Contains(".itemBackdrop:before{", StringComparison.Ordinal))
                {
                    layout = Path.GetFileName(file);
                }

                if (site is not null && layout is not null)
                {
                    break;
                }
            }

            var chunks = new[] { site, layout }.OfType<string>().Distinct(StringComparer.Ordinal);
            return string.Join('\n', links.Concat(chunks).Concat(routes).Select(href => "@import url(\"../web/" + href + "\");"));
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // The web client's route chunks: search, the item details page and the item list pages.
    private static readonly string[] RouteStyles = ["search", "itemDetails", "list"];

    [GeneratedRegex(@"^[A-Za-z]+\.[0-9a-f]+\.css$")]
    private static partial Regex RouteStylesheet();

    [GeneratedRegex(@"@font-face\{[^}]*font-family:Noto Sans;[^}]*\}")]
    private static partial Regex NotoFontFace();

    [GeneratedRegex(@"url\((?!data:|https?:|/)([^)""']+)\)")]
    private static partial Regex RelativeUrl();

    [GeneratedRegex(@"html(?:\[lang\|=[A-Za-z-]+\])?\{font-family:[^}]*\}")]
    private static partial Regex FontStack();

    [GeneratedRegex(@"^[0-9]+\.[0-9a-f]+\.css$")]
    private static partial Regex ChunkStylesheet();

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"href=""([^""]+\.css(?:\?[^""]*)?)""", RegexOptions.IgnoreCase)]
    private static partial Regex StylesheetHref();

    /// <summary>
    /// Serves a TMDb poster, backdrop or photo from the server's cache folder, read from TMDb the first time. One the
    /// server can't keep is sent on to TMDb's own address.
    /// </summary>
    /// <param name="size">The size, such as w342.</param>
    /// <param name="file">The file, such as abc123.jpg.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The image, a redirect to TMDb, or not found.</returns>
    [HttpGet("img/{size}/{file}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Artwork([FromRoute] string size, [FromRoute] string file, CancellationToken cancellationToken)
    {
        if (ArtworkCache.Source(size, file) is not { } source)
        {
            return NotFound();
        }

        if (await _artwork.GetAsync(size, file, cancellationToken).ConfigureAwait(false) is not { } path)
        {
            return Redirect(source);
        }

        // TMDb's image addresses never change what they show.
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var type = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(path, type);
    }

    /// <summary>Serves TMDb's logo, which the page and the settings show as TMDb's terms ask of apps using its data.</summary>
    /// <returns>The logo.</returns>
    [HttpGet("tmdb.svg")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("image/svg+xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TmdbLogo()
    {
        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return Content(EmbeddedResources.Read("necto_requests_tmdb.svg"), "image/svg+xml");
    }

    /// <summary>Serves the request page's web app manifest.</summary>
    /// <returns>The manifest.</returns>
    [HttpGet("manifest.webmanifest")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("application/manifest+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WebManifest()
    {
        CacheAsset(TimeSpan.FromDays(1));
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var manifest = EmbeddedResources.Read("necto_requests_manifest.webmanifest");
        return Content(IsIssuesPath ? AsIssuesManifest(manifest) : manifest, "application/manifest+json");
    }

    /// <summary>Serves the request page's service worker.</summary>
    /// <returns>The script.</returns>
    [HttpGet("sw.js")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ServiceWorker()
    {
        return WebClientApp.ServeWorker(Response, WebClientApp.ServiceWorker("necto-requests"));
    }

    /// <summary>Serves Stolon's web push script, which the request page's service worker loads to show browser notifications.</summary>
    /// <returns>The script.</returns>
    [HttpGet("push-worker.js")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult PushWorker()
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return WebPushWorker.Serve(Response);
    }

    /// <summary>Serves Stolon's page script, which the request page uses for its headings' casing and browser notifications.</summary>
    /// <returns>The script.</returns>
    [HttpGet("stolon-page.js")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("text/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult StolonPage()
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return StolonPageScript.Serve(Response);
    }

    internal static readonly string[] AppIcons = ["requests-180.png", "requests-192.png", "requests-512.png", "requests-maskable-512.png"];

    /// <summary>Serves one of the request page's app icons.</summary>
    /// <param name="name">The icon's file name.</param>
    /// <returns>The icon, or not found.</returns>
    [HttpGet("icons/{name}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult AppIcon([FromRoute] string name)
    {
        if (!AppIcons.Contains(name, StringComparer.Ordinal) || EmbeddedResources.OpenIcon(name) is not { } icon)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(icon, "image/png");
    }

    /// <summary>Serves the web client's logo mark for the page's app bar.</summary>
    /// <returns>The image, or not found.</returns>
    [HttpGet("logo.png")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Logo()
    {
        if (_logoPath.Get(() => FindLogo(_paths.WebPath)) is not { } logo)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "public, max-age=86400";
        return PhysicalFile(logo, "image/png");
    }

    // The mark the web client's app bar shows, named with a build hash, so it is looked up in the installed web client.
    internal static string? FindLogo(string? webPath)
    {
        try
        {
            return string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath)
                ? null
                : Directory.EnumerateFiles(webPath, "icon-transparent*.png", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.EnumerateFiles(webPath, "touchicon.png", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The web client's @font-face rules for Noto Sans itself (not its CJK siblings), with each url() made relative to
    // /requests/ so it loads the web client's file, then its font stacks for the page (html, and by language for CJK).
    internal static string FindFonts(string? webPath)
    {
        try
        {
            if (string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath))
            {
                return string.Empty;
            }

            foreach (var file in Directory.EnumerateFiles(webPath, "*.css"))
            {
                var css = System.IO.File.ReadAllText(file);
                if (!css.Contains("font-family:Noto Sans;", StringComparison.Ordinal))
                {
                    continue;
                }

                var rules = NotoFontFace().Matches(css).Select(m => RelativeUrl().Replace(m.Value, "url(../web/$1)"));
                var stacks = FontStack().Matches(css).Select(m => m.Value);
                return string.Join('\n', rules.Concat(stacks));
            }
        }
        catch (IOException)
        {
            // No fonts then; the page falls back to the system's sans-serif.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return string.Empty;
    }

    // ------------------------------------------------------------------ signed-in users

    /// <summary>Reports who the signed-in user is and what they may do.</summary>
    /// <returns>The user's access.</returns>
    [HttpGet("/Requests/Me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<UserAccess> Me()
    {
        return GetAccess() is { } access ? Ok(access) : Forbid();
    }

    /// <summary>Searches for movies and shows to request.</summary>
    /// <param name="searchTerm">The title to search for.</param>
    /// <param name="query">An alias of searchTerm.</param>
    /// <param name="type">Movie or Series to search only one type; both when empty.</param>
    /// <param name="page">The page (skip and take).</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <param name="filters">What the titles are narrowed to: inLibrary, states, types and genres.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of the results, marked with what the library and the requests already hold.</returns>
    [HttpGet("/Requests/Search")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RequestsQueryResult<SearchItemDto>>> Search([FromQuery] string? searchTerm, [FromQuery] string? query, [FromQuery] RequestType? type, [FromQuery] PagedQuery page, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] TitleFilterQuery filters, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        var types = new List<RequestType>();
        if (access.Movies && type is null or RequestType.Movie)
        {
            types.Add(RequestType.Movie);
        }

        if (access.Series && type is null or RequestType.Series)
        {
            types.Add(RequestType.Series);
        }

        var trimmed = (searchTerm ?? query ?? string.Empty).Trim();
        var results = await _search.SearchAsync(trimmed.Length > 200 ? trimmed[..200] : trimmed, types, cancellationToken, access).ConfigureAwait(false);
        if (filters?.ToFilter() is { IsEmpty: false } filter)
        {
            results = results.Where(r => filter.Matches(r, access.IsAdministrator)).ToList();
        }

        return Ok(MapAll(Slice(Outsiders(results, access), Paging(page, startIndex, limit, null, null)), found => _items.For(found, access.User)));
    }

    /// <summary>
    /// Returns the rows the search page shows before anything is searched for, in the settings' order: Recently Added and
    /// Recommended for You, made for the viewer, then TMDb's lists, marked as search results are. Only the rows for types
    /// the viewer may request, and only those with titles.
    /// </summary>
    /// <param name="filters">What the titles are narrowed to: inLibrary, states, types and genres.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows.</returns>
    [HttpGet("/Requests/Discover")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<DiscoverRowDto>>> Discover([FromQuery] TitleFilterQuery filters, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        // Recently Added is the library's own, so Hide available titles leaves it whole: it's narrowed by the viewer's
        // filter alone, read before the setting's is added.
        var own = filters?.ToFilter();
        var rows = await _search.DiscoverAsync(access, cancellationToken, RowFilters(filters).ToFilter(), own).ConfigureAwait(false);

        // Every row's titles built at once, then dealt back out to their rows in order.
        var built = _items.For(rows.SelectMany(r => Outsiders(r.Items, access)).ToList(), access.User);
        var dealt = new List<DiscoverRowDto>(rows.Count);
        var at = 0;
        foreach (var row in rows)
        {
            dealt.Add(new DiscoverRowDto(row.Key, row.Title, built.GetRange(at, row.Items.Count)));
            at += row.Items.Count;
        }

        return Ok(dealt);
    }

    /// <summary>
    /// Reads further along one of the empty search's rows (Recently Added, Recommended for You, or one of TMDb's), a page
    /// at a time, marked as search results are. Requests/Discover gives each row's start.
    /// </summary>
    /// <param name="key">The row's key.</param>
    /// <param name="page">The page (skip and take).</param>
    /// <param name="startIndex">Where the page starts: 0, or the last page's NextStartIndex.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="filters">What the titles are narrowed to: inLibrary, states, types and genres.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page, with NextStartIndex to carry on from; not found for a row there isn't.</returns>
    [HttpGet("/Requests/Discover/{key}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RequestsQueryResult<SearchItemDto>>> DiscoverList([FromRoute] string key, [FromQuery] PagedQuery page, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] TitleFilterQuery filters, CancellationToken cancellationToken)
        => SearchService.IsViewerRow(key)
            ? ViewerList(key, Paging(page, startIndex, limit, null, null), filters?.ToFilter(), cancellationToken)
            : ListPage(_search.ListFor(key ?? string.Empty), Paging(page, startIndex, limit, null, null), RowFilters(filters).ToFilter(), cancellationToken);

    // MARK: ViewerList

    // A page of a row made for the viewer (Recently Added, Recommended for You). Neither leaves out what the library
    // has for Hide available titles: Recently Added is the library's, and Recommended for You leaves it out itself.
    private async Task<ActionResult<RequestsQueryResult<SearchItemDto>>> ViewerList(string key, PagedQuery query, TitleFilter? filter, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        var (skip, take) = RequestDatabase.Window(query);
        if (await _search.ViewerListAsync(key, access, skip, Math.Min(take, MaxListPage), cancellationToken, filter).ConfigureAwait(false) is not { } found)
        {
            return NotFound();
        }

        return Ok(new RequestsQueryResult<SearchItemDto>(skip, found.Total, _items.For(Outsiders(found.Items, access), access.User))
        {
            NextStartIndex = found.Next
        });
    }

    // The home rows' filters: with Hide available on, only titles the library doesn't have all of, unless the viewer
    // filtered by that themselves.
    private static TitleFilterQuery RowFilters(TitleFilterQuery? filters)
    {
        filters ??= new TitleFilterQuery();
        if (filters.InLibrary is null && RequestConfig.Instance?.ReadConfiguration(c => c.HideAvailableInRows) == true)
        {
            filters.InLibrary = false;
        }

        return filters;
    }

    /// <summary>
    /// Lists movies or shows with a genre, from a studio, or on a network (shows), most popular first, from TMDb through
    /// Jellyfin's TMDb access, a page at a time, marked as search results are. A title's details give what it can be
    /// browsed by.
    /// </summary>
    /// <param name="type">Movie or Series.</param>
    /// <param name="kind">Genre, Company or Network.</param>
    /// <param name="id">Its TMDb id.</param>
    /// <param name="page">The page (skip and take).</param>
    /// <param name="startIndex">Where the page starts: 0, or the last page's NextStartIndex.</param>
    /// <param name="limit">How many to return.</param>
    /// <param name="filters">What the titles are narrowed to: inLibrary, states, types and genres.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The page, with NextStartIndex to carry on from.</returns>
    [HttpGet("/Requests/Browse")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<RequestsQueryResult<SearchItemDto>>> Browse([FromQuery] RequestType type, [FromQuery] string? kind, [FromQuery] int id, [FromQuery] PagedQuery page, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] TitleFilterQuery filters, CancellationToken cancellationToken)
        => Enum.IsDefined(type) && SearchService.BrowseList(type, kind ?? string.Empty, id) is { } list
            ? ListPage(list, Paging(page, startIndex, limit, null, null), filters?.ToFilter(), cancellationToken)
            : Task.FromResult<ActionResult<RequestsQueryResult<SearchItemDto>>>(BadRequest());

    /// <summary>
    /// Gets one movie or show by its TMDb id, as search results give it: whether the library has it for the viewer, and
    /// whether it's requested, waiting, downloaded or blocked.
    /// </summary>
    /// <param name="type">Movie or Series.</param>
    /// <param name="tmdbId">Its TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The title.</returns>
    [HttpGet("/Requests/Titles/{type}/{tmdbId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchItemDto>> Title([FromRoute] RequestType type, [FromRoute] string tmdbId, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (!Enum.IsDefined(type) || string.IsNullOrEmpty(tmdbId) || tmdbId.Length > 10 || !tmdbId.All(char.IsAsciiDigit)
            || !(type == RequestType.Movie ? access.Movies : access.Series)
            || await _search.TitleAsync(type, new Dictionary<string, string> { [ProviderIds.Tmdb] = tmdbId }, access, cancellationToken).ConfigureAwait(false) is not { } title)
        {
            return NotFound();
        }

        return Ok(Outsiders(new List<SearchResult> { title }, access).Select(i => _items.For(i, access.User)).First());
    }

    /// <summary>Returns titles like one, from TMDb's recommendations, marked as search results are: More Like This for a title not in the library.</summary>
    /// <param name="lookup">The title: its type and ids.</param>
    /// <param name="filters">What the titles are narrowed to: inLibrary, states, types and genres.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The titles; empty when there are none.</returns>
    [HttpPost("/Requests/Titles/Similar")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<SearchItemDto>>> Similar([FromBody] DetailsLookup lookup, [FromQuery] TitleFilterQuery filters, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (lookup is null || !Enum.IsDefined(lookup.Type) || lookup.Type is not (RequestType.Movie or RequestType.Series))
        {
            return BadRequest();
        }

        var items = (access.Movies && lookup.Type == RequestType.Movie) || (access.Series && lookup.Type == RequestType.Series)
            ? await _search.SimilarAsync(lookup.Type, lookup.ProviderIds ?? new Dictionary<string, string>(), access, cancellationToken, filters?.ToFilter()).ConfigureAwait(false)
            : new List<SearchResult>();
        return Ok(_items.For(Outsiders(items, access), access.User));
    }

    /// <summary>
    /// Returns a TMDb collection of movies, such as Dune Collection, with each movie as search results show them.
    /// </summary>
    /// <param name="id">The collection's TMDb id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection, or not found.</returns>
    [HttpGet("/Requests/Collections/{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CollectionDto>> Collection([FromRoute] int id, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true, Movies: true } access)
        {
            return Forbid();
        }

        if (await _search.CollectionAsync(id, access, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            return NotFound();
        }

        // Counted before what requesters may not see of Radarr and Sonarr is taken off.
        var requestable = found.Items.Count(Requestable);
        return Ok(new CollectionDto
        {
            Id = found.Set.Id,
            Name = found.Set.Name,
            Overview = found.Set.Overview,
            PosterUrl = found.Set.PosterUrl,
            BackdropUrl = found.Set.BackdropUrl,
            Requestable = requestable,
            Items = _items.For(Outsiders(found.Items, access), access.User)
        });
    }

    /// <summary>
    /// Requests every movie of a collection that can be: not in the library, not requested, not on its way already and
    /// not blocked. Each is a request of its own, held to the same rules and limits as one made alone.
    /// </summary>
    /// <param name="id">The collection's TMDb id.</param>
    /// <param name="body">The profile and note each is requested with, as the request form gives them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many were requested, skipped and refused, with the first refusal's reason.</returns>
    [HttpPost("/Requests/Collections/{id:int}/Request")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CollectionRequestResult>> RequestCollection([FromRoute] int id, [FromBody] CollectionRequest? body, CancellationToken cancellationToken)
    {
        // For someone else, by an administrator, as a single request can be.
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        if (access.UserId == Guid.Empty)
        {
            return BadRequest(new { Error = "Name the user to request for with userId." });
        }

        if (!access.CanRequest || !access.Movies)
        {
            return Forbid();
        }

        if (await _search.CollectionAsync(id, access, cancellationToken).ConfigureAwait(false) is not { } found)
        {
            return NotFound();
        }

        return Ok(await RequestEach(
            found.Items,
            movie => _requests.SubmitAsync(access, new NewMediaRequest
            {
                Type = RequestType.Movie,
                Title = movie.Title,
                Year = movie.Year,
                Overview = movie.Overview,
                PosterUrl = movie.PosterUrl,
                BackdropUrl = movie.BackdropUrl,
                ProviderIds = movie.ProviderIds,
                Note = body?.Note,
                ProfileId = body?.ProfileId
            }, cancellationToken, sendWait: TimeSpan.Zero),
            CollectionAtOnce,
            cancellationToken).ConfigureAwait(false));
    }

    // How many of a collection's movies are requested at once: a collection of a dozen then doesn't wait on each one's
    // checks and lookups in turn, while the server and Radarr aren't handed all of them at the same moment.
    private const int CollectionAtOnce = 4;

    // Requests each movie of a collection that can be, a few at a time. Each request's own checks (the limits, whether
    // it's already asked for) run inside the database's lock, so making them side by side is as safe as one by one.
    // Counted afterwards in the collection's order, so the first refusal reported is the first movie's that was refused.
    internal static async Task<CollectionRequestResult> RequestEach(IReadOnlyList<SearchResult> movies, Func<SearchResult, Task<(MediaRequest? Request, string? Error)>> submit, int atOnce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movies);
        ArgumentNullException.ThrowIfNull(submit);
        var outcomes = new (bool Skipped, bool Made, string? Error)[movies.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, atOnce));
        await Task.WhenAll(movies.Select(async (movie, i) =>
        {
            if (!Requestable(movie))
            {
                outcomes[i] = (true, false, null);
                return;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var (made, error) = await submit(movie).ConfigureAwait(false);
                outcomes[i] = (false, made is not null, error);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        var result = new CollectionRequestResult();
        foreach (var (skipped, made, error) in outcomes)
        {
            if (skipped)
            {
                result.Skipped++;
            }
            else if (made)
            {
                result.Done++;
            }
            else
            {
                result.Failed++;
                result.Error ??= error;
            }
        }

        return result;
    }

    // A movie of a collection that can still be asked for: not in the library, not requested, not already in Radarr and
    // not blocked.
    private static bool Requestable(SearchResult movie)
        => movie.LibraryItemId is null && movie.RequestId is null && !movie.InArr && !movie.Downloaded && !movie.Blocked;

    /// <summary>Lists the streaming services titles can be filtered by in the server's country, most prominent first.</summary>
    /// <param name="type">Movie or Series.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The services; empty without TMDb.</returns>
    [HttpGet("/Requests/Providers")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<TmdbChoice>>> Providers([FromQuery] RequestType type, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true })
        {
            return Forbid();
        }

        return Ok(await _search.ProvidersAsync(Enum.IsDefined(type) ? type : RequestType.Movie, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Finds what a home row can pick its titles by: a type's genres or streaming services, or TMDb's keywords or
    /// studios by name.
    /// </summary>
    /// <param name="kind">Genre, Keyword, Company or Provider.</param>
    /// <param name="type">Movie or Series.</param>
    /// <param name="query">For keywords and studios, the name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The choices.</returns>
    [HttpGet("/Requests/HomeRows/Choices")]
    [Authorize(Policy = AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TmdbChoice>>> HomeRowChoices([FromQuery] string? kind, [FromQuery] RequestType type, [FromQuery] string? query, CancellationToken cancellationToken)
        => Ok(await _search.ChoicesAsync(kind ?? string.Empty, Enum.IsDefined(type) ? type : RequestType.Movie, query, cancellationToken).ConfigureAwait(false));

    /// <summary>Lists the genres titles can be filtered by, as TMDb names them, for movies, shows or both.</summary>
    /// <param name="type">Movie or Series; both when empty.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The genres, sorted; empty without TMDb.</returns>
    [HttpGet("/Requests/Genres")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<string>>> Genres([FromQuery] RequestType? type, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        var types = new[] { RequestType.Movie, RequestType.Series }
            .Where(t => (type is null || type == t) && (t == RequestType.Movie ? access.Movies : access.Series));
        return Ok(await _search.GenreNamesAsync(types, cancellationToken).ConfigureAwait(false));
    }

    // The most titles one read of a TMDb list returns.
    private const int MaxListPage = 100;

    // A page of a TMDb list for the viewer. Only the types they may request; which server has what is for administrators.
    private async Task<ActionResult<RequestsQueryResult<SearchItemDto>>> ListPage(TmdbList? list, PagedQuery query, TitleFilter? filter, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (list is null || !(list.Type == RequestType.Movie ? access.Movies : access.Series))
        {
            return NotFound();
        }

        // At most MaxListPage at a time: each title read is looked up in the library, Radarr or Sonarr. TMDb has
        // nothing past its last page.
        var (skip, take) = RequestDatabase.Window(query);
        if (skip >= TmdbDiscovery.LastPage * TmdbDiscovery.PageSize)
        {
            return Ok(new RequestsQueryResult<SearchItemDto>(skip, 0, new List<SearchItemDto>()));
        }

        if (await _search.ListAsync(list, access, skip, Math.Min(take, MaxListPage), cancellationToken, filter).ConfigureAwait(false) is not { } found)
        {
            return NotFound();
        }

        return Ok(new RequestsQueryResult<SearchItemDto>(skip, found.Total, _items.For(Outsiders(found.Items, access), access.User))
        {
            NextStartIndex = found.Next
        });
    }

    // Which server has what is for administrators only; everyone else sees Downloaded, and that it's on its way (InArr
    // says neither which server nor how far along).
    private static List<SearchResult> Outsiders(List<SearchResult> items, UserAccess access)
    {
        if (!access.IsAdministrator)
        {
            foreach (var result in items)
            {
                result.ArrHasFiles = false;
            }
        }

        return items;
    }

    /// <summary>Returns a title's tagline, cast and crew for its details page.</summary>
    /// <param name="lookup">The title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The details.</returns>
    [HttpPost("/Requests/Titles/Details")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TitleDetails>> Details([FromBody] DetailsLookup lookup, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (lookup is null || !Enum.IsDefined(lookup.Type))
        {
            return BadRequest();
        }

        lookup.Title = Cap(lookup.Title, 300);
        // The details and what the title can be browsed by come from different places, so both are asked at once.
        var browse = _search.BrowseLinksAsync(lookup.Type, lookup.ProviderIds ?? new Dictionary<string, string>(), cancellationToken);
        var details = await _details.GetTitleAsync(lookup, cancellationToken, access.User).ConfigureAwait(false);
        details.Browse = await browse.ConfigureAwait(false);
        var config = RequestConfig.Instance?.ReadConfiguration(c => c);
        return Ok(config is null ? details : WithBlocklist(details, lookup, config, access.IsAdministrator));
    }

    // Whether a title can't be requested by the blocklist, by the same rule a request for it is refused by: the title
    // itself, or its genre, rating, TMDb keyword, language, collection or a word in its name (as the viewer or the
    // providers name it), so the page doesn't offer Request for something the submit would refuse. Administrators, who
    // can unblock it, are also told whether the title itself is on the list (TitleBlocked) and why it is blocked.
    internal static TitleDetails WithBlocklist(TitleDetails details, DetailsLookup lookup, RequestSettings config, bool isAdministrator)
    {
        var ids = ProviderIds.Clean(lookup.ProviderIds);
        var titleBlocked = ids.Count > 0 && config.BlockedTitles.Any(t => Blocklist.Matches(t, lookup.Type, ids));
        var reason = Blocklist.Reason(config, lookup.Type, ids, lookup.Title, details.Genres, details.OfficialRating, details.Traits)
            ?? (string.IsNullOrEmpty(details.Name) ? null : Blocklist.Reason(config, lookup.Type, ids, details.Name, details.Genres, details.OfficialRating, details.Traits));
        return details.WithBlocked(titleBlocked, reason, isAdministrator);
    }

    /// <summary>Finds people by name, for searching by actor.</summary>
    /// <param name="searchTerm">The name.</param>
    /// <param name="query">An alias of searchTerm.</param>
    /// <param name="page">The page (skip and take).</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of the people.</returns>
    [HttpGet("/Requests/Persons")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RequestsQueryResult<PersonItemDto>>> People([FromQuery] string? searchTerm, [FromQuery] string? query, [FromQuery] PagedQuery page, [FromQuery] int? startIndex, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        var trimmed = (searchTerm ?? query ?? string.Empty).Trim();
        var people = await _details.SearchPeopleAsync(trimmed.Length > 100 ? trimmed[..100] : trimmed, cancellationToken, access.User).ConfigureAwait(false);
        return Ok(Map(Slice(people, Paging(page, startIndex, limit, null, null)), _items.For));
    }

    /// <summary>Returns a person's page: biography, photo and their titles in the library.</summary>
    /// <param name="lookup">The person.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The person.</returns>
    [HttpPost("/Requests/Persons/Details")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonDetails>> Person([FromBody] PersonLookup lookup, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (lookup is null)
        {
            return BadRequest();
        }

        // A person is looked up and cached by these, so they are held to what a real name and TMDb id look like.
        lookup.Name = Cap(lookup.Name, 200);
        lookup.TmdbId = lookup.TmdbId is { Length: > 0 and <= 10 } tmdb && tmdb.All(char.IsAsciiDigit) ? tmdb : null;
        lookup.ImageUrl = lookup.ImageUrl is { Length: <= 500 } image ? image : null;
        var person = await _details.GetPersonAsync(lookup, cancellationToken, access.User).ConfigureAwait(false);

        // The rest of what they're in, of the types the viewer may request, marked for them; what the library has for
        // them is in its own row already.
        var credits = person.Credits.Where(c => c.Type == RequestType.Movie ? access.Movies : access.Series).ToList();
        var marked = await _search.MarkTitlesAsync(credits, access, cancellationToken).ConfigureAwait(false);
        person.Credits = Outsiders(marked.Where(c => c.LibraryItemId is null).ToList(), access);

        // One the library has that Jellyfin hasn't credited them in joins the library's row rather than leaving both.
        var known = person.LibraryTitles.Select(t => t.LibraryItemId).ToHashSet();
        person.LibraryTitles.AddRange(Outsiders(marked.Where(c => c.LibraryItemId is { } id && known.Add(id)).ToList(), access));
        return Ok(person);
    }

    /// <summary>Lists a season's episodes, from TMDb, with their air dates and which the library has.</summary>
    /// <param name="lookup">The show, by its ids, title and year, and the season's number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The season, or not found.</returns>
    [HttpPost("/Requests/Titles/Episodes")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeasonEpisodes>> Episodes([FromBody] EpisodesLookup lookup, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true, Series: true } access)
        {
            return Forbid();
        }

        if (lookup is null)
        {
            return BadRequest();
        }

        lookup.Title = Cap(lookup.Title, 300);
        return await _search.EpisodesAsync(lookup, access, cancellationToken).ConfigureAwait(false) is { } season ? Ok(season) : NotFound();
    }

    /// <summary>Lists a show's seasons with what the library and the requests already cover.</summary>
    /// <param name="show">The show, by its ids and title.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The seasons.</returns>
    [HttpPost("/Requests/Titles/Seasons")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeriesDetails>> Seasons([FromBody] SeriesLookup show, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { CanRequest: true, Series: true } access)
        {
            return Forbid();
        }

        if (show is null)
        {
            return BadRequest();
        }

        show.Title = Cap(show.Title, 300);
        return Ok(await _requests.GetSeriesDetailsAsync(show, cancellationToken, access).ConfigureAwait(false));
    }

    /// <summary>Lists the signed-in user's own requests, and those they joined, a page at a time, newest first unless sorted by status.</summary>
    /// <param name="query">The page (skip and take); the filter is a status name, several separated by commas, or Open.</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <param name="status">The same as the filter, as named elsewhere in the API.</param>
    /// <param name="sort"><c>status</c> to order them by where they stand, then by title.</param>
    /// <param name="type">Only movies (Movie) or shows (Series), when set.</param>
    /// <param name="genres">Only those with one of these genres, separated by |, when set.</param>
    /// <param name="scheduled">With everyone's scheduled ones too (see Scheduled): every request the viewer may see.</param>
    /// <returns>The page.</returns>
    [HttpGet("/Requests/Mine")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<RequestsQueryResult<RequestItemDto>> Mine([FromQuery] PagedQuery query, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] string? status, [FromQuery] string? sort, [FromQuery] RequestType? type = null, [FromQuery] string? genres = null, [FromQuery] bool scheduled = false)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        query = Paging(query, startIndex, limit, null, status);
        query.Search = null;
        // Other people's scheduled ones only of the types the viewer may request, as Scheduled lists them.
        var withScheduled = scheduled && access.CanRequest && (access.Movies || access.Series)
            ? (On: true, Type: access.Movies && access.Series ? (RequestType?)null : access.Movies ? RequestType.Movie : RequestType.Series)
            : (On: false, Type: null);
        var page = _db.Page(query, access.UserId, byStatus: string.Equals(sort, "status", StringComparison.OrdinalIgnoreCase), type: type, withJoined: true, genres: GenreList(genres), withScheduled: withScheduled);
        if (!withScheduled.On || access.IsAdministrator)
        {
            return Ok(Items(page.Items, page.TotalCount, query, access));
        }

        // Someone else's, unless the viewer joined it, is left out when its title is in a library the viewer can't see.
        var sight = SightOf(page.Items, access);
        return Ok(Visible(page, r => r.UserId == access.UserId || r.Joined?.Any(j => j.UserId == access.UserId) == true || r.LibraryItemId is not { } item || sight.CanSee(item), query, access, sight));
    }

    /// <summary>
    /// Lists everyone's approved requests not yet all in the library, a page at a time, by where they stand and then by
    /// title: which everyone who may request sees on the request page's Requests page, of the types they may request and
    /// leaving out any Radarr or Sonarr dropped. Who made each is shown only to those allowed to see requesters, other
    /// people's notes never are, and a title in a library the viewer can't see is left out.
    /// </summary>
    /// <param name="query">The page (skip and take); its filter and search are ignored.</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <param name="others">Only other people's, leaving out the viewer's own.</param>
    /// <param name="type">Only movies (Movie) or shows (Series), when set.</param>
    /// <param name="genres">Only those with one of these genres, separated by |, when set.</param>
    /// <param name="status">Only these of its statuses, separated by commas, when set.</param>
    /// <returns>The page.</returns>
    [HttpGet("/Requests/Scheduled")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<RequestsQueryResult<RequestItemDto>> Scheduled([FromQuery] PagedQuery query, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] bool others = false, [FromQuery] RequestType? type = null, [FromQuery] string? genres = null, [FromQuery] string? status = null)
    {
        query = Paging(query, startIndex, limit, null, null);
        if (GetAccess() is not { CanRequest: true } access)
        {
            return Forbid();
        }

        if (!access.Movies && !access.Series)
        {
            return Ok(new RequestsQueryResult<RequestItemDto>(0, 0, Array.Empty<RequestItemDto>()));
        }

        query.Search = null;

        // Its own statuses, narrowed to those asked for.
        var asked = (status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var statuses = RequestDatabase.ScheduledStatuses.Where(s => asked.Length == 0 || asked.Contains(s.ToString(), StringComparer.OrdinalIgnoreCase)).ToList();
        if (statuses.Count == 0)
        {
            return Ok(new RequestsQueryResult<RequestItemDto>(0, 0, Array.Empty<RequestItemDto>()));
        }

        query.Filter = string.Join(',', statuses);
        // Of the types they may request, and of the one asked for.
        var mayType = access.Movies && access.Series ? (RequestType?)null : access.Movies ? RequestType.Movie : RequestType.Series;
        if (type is not null && mayType is not null && type != mayType)
        {
            return Ok(new RequestsQueryResult<RequestItemDto>(0, 0, Array.Empty<RequestItemDto>()));
        }

        var page = _db.Page(query, exceptUserId: others ? access.UserId : null, byStatus: true, type: type ?? mayType, liveOnly: true, genres: GenreList(genres));
        var sight = SightOf(page.Items, access);
        return Ok(Visible(page, r => access.IsAdministrator || r.LibraryItemId is not { } item || sight.CanSee(item), query, access, sight));
    }

    /// <summary>Makes a request.</summary>
    /// <param name="submit">The title picked from the search results, with seasons and a note.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new request, or an error to show.</returns>
    [HttpPost("/Requests")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RequestItemDto>> Submit([FromBody] NewMediaRequest submit, CancellationToken cancellationToken)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        if (submit is null)
        {
            return BadRequest(new { Error = StolonTranslations.Get(TranslationKeys.RequestsErrorNothingRequested, Language) });
        }

        // An API key is no one in particular: it requests for the user it names.
        if (access.UserId == Guid.Empty)
        {
            return BadRequest(new { Error = "Name the user to request for with userId." });
        }

        var (request, error) = await _requests.SubmitAsync(access, submit, cancellationToken).ConfigureAwait(false);
        return request is null ? BadRequest(new { Error = error }) : Ok(Item(request, access));
    }

    /// <summary>
    /// Changes a request while it waits for approval: its seasons, profile and note, held to what a new request is, as
    /// Jellyfin changes an item with POST /Items/{itemId}. The caller's own, or anyone's for an administrator.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="edit">What they picked.</param>
    /// <returns>The changed request; a refusal; or not found when they may not change it or it no longer waits.</returns>
    [HttpPost("/Requests/{requestId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> Edit([FromRoute(Name = "requestId")] Guid id, [FromBody] MediaRequestEdit edit)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        if (edit is null)
        {
            return BadRequest(new { Error = StolonTranslations.Get(TranslationKeys.RequestsErrorNothingChanged, Language) });
        }

        var (request, error) = _requests.Edit(access, id, edit);
        return request is not null ? Ok(Item(request, access))
            : error is not null ? BadRequest(new { Error = error })
            : NotFound();
    }

    /// <summary>
    /// Joins someone else's open request for a title the caller wants too: they're told about it as its requester is,
    /// and it shows among their requests. It costs them nothing.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <returns>The request; a refusal; or not found when there's no such open request.</returns>
    [HttpPost("/Requests/{requestId:guid}/Join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> Join([FromRoute(Name = "requestId")] Guid id) => Joining(id, true);

    /// <summary>Leaves a request the caller joined.</summary>
    /// <param name="id">The request id.</param>
    /// <returns>The request, or not found when there's no such open request.</returns>
    [HttpDelete("/Requests/{requestId:guid}/Join")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> Leave([FromRoute(Name = "requestId")] Guid id) => Joining(id, false);

    private ActionResult<RequestItemDto> Joining(Guid id, bool join)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        if (access.UserId == Guid.Empty)
        {
            return BadRequest(new { Error = "Name the user to " + (join ? "join" : "leave") + " as with userId." });
        }

        var (request, error) = _requests.Join(access, id, join);
        return request is not null ? Ok(Item(request, access))
            : error is not null ? BadRequest(new { Error = error })
            : NotFound();
    }

    /// <summary>
    /// Returns one request: the caller's own, or anyone's for administrators and those who see everyone's requests, as
    /// the lists show it to them.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <returns>The request, or not found.</returns>
    [HttpGet("/Requests/{requestId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> One([FromRoute(Name = "requestId")] Guid id)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        // Someone else's is not found unless they joined it or may see everyone's, and then (unless they joined it, as
        // their own list shows it) not when it's only in a library they can't see.
        if (_db.Get(id) is not { } request
            || (request.UserId != access.UserId && !access.IsAdministrator
                && request.Joined?.Any(j => j.UserId == access.UserId) != true
                && (!access.SeeAllRequests || (request.LibraryItemId is { } item && !_requests.CanSee(item, access)))))
        {
            return NotFound();
        }

        return Ok(Item(request, access));
    }

    /// <summary>Removes a request: the requester's own while pending or once Radarr or Sonarr has dropped it, or any for an administrator.</summary>
    /// <param name="id">The request id.</param>
    /// <returns>No content, or not found.</returns>
    [HttpDelete("/Requests/{requestId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Remove([FromRoute(Name = "requestId")] Guid id)
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        return _requests.Remove(id, access) ? NoContent() : NotFound();
    }

    // ------------------------------------------------------------------ administrators

    /// <summary>
    /// Lists everyone's requests a page at a time, newest first, for the dashboard table and the request page's Requests page:
    /// administrators and users allowed to see everyone's requests. Those users see who made each only when
    /// allowed to, and never other people's notes, so they can't search by either.
    /// </summary>
    /// <param name="query">The page; the filter is a status name or Open, the search matches title, requester and note.</param>
    /// <param name="user">Only this requester's, when set.</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <param name="searchTerm">What to search for, as Jellyfin names it; instead of search.</param>
    /// <param name="status">A status name, a list of them separated by commas, or Open; instead of filter.</param>
    /// <param name="others">Only other people's, leaving out the viewer's own.</param>
    /// <param name="sort"><c>status</c> to order them by where they stand, then by title, as the request page lists them.</param>
    /// <param name="type">Only movies (Movie) or shows (Series), when set.</param>
    /// <param name="genres">Only those with one of these genres, separated by |, when set.</param>
    /// <returns>The page.</returns>
    [HttpGet("/Requests/All")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<RequestsQueryResult<RequestItemDto>> All([FromQuery] PagedQuery query, [FromQuery] Guid? user, [FromQuery] int? startIndex, [FromQuery] int? limit, [FromQuery] string? searchTerm, [FromQuery] string? status, [FromQuery] bool others = false, [FromQuery] string? sort = null, [FromQuery] RequestType? type = null, [FromQuery] string? genres = null)
    {
        query = Paging(query, startIndex, limit, searchTerm, status);
        if (GetAccess() is not { } access || !(access.IsAdministrator || access.SeeAllRequests))
        {
            return Forbid();
        }

        var except = others ? access.UserId : (Guid?)null;
        var byStatus = string.Equals(sort, "status", StringComparison.OrdinalIgnoreCase);
        if (access.IsAdministrator)
        {
            var all = _db.Page(query, user, except, byStatus, type, genres: GenreList(genres));
            return Ok(Items(all.Items, all.TotalCount, query, access));
        }

        query.Search = null;
        var page = _db.Page(query, access.SeeRequesters ? user : null, except, byStatus, type, genres: GenreList(genres));

        // A title in a library the viewer can't see is left out, so they never learn of it. Their own and those they
        // joined always show, as on their own list.
        var sight = SightOf(page.Items, access);
        return Ok(Visible(page, r => r.UserId == access.UserId || r.Joined?.Any(j => j.UserId == access.UserId) == true || r.LibraryItemId is not { } item || sight.CanSee(item), query, access, sight));
    }

    /// <summary>Lists everyone who has made requests, for filtering the list by requester.</summary>
    /// <param name="query">The page (skip and take).</param>
    /// <param name="startIndex">Where the page starts, as Jellyfin names it; instead of skip.</param>
    /// <param name="limit">How many to return, as Jellyfin names it; instead of take.</param>
    /// <returns>A page of the requesters, by name.</returns>
    [HttpGet("/Requests/Requesters")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RequestsQueryResult<Requester>> Requesters([FromQuery] PagedQuery query, [FromQuery] int? startIndex, [FromQuery] int? limit)
    {
        return Ok(Slice(_db.Requesters(), Paging(query, startIndex, limit, null, null)));
    }

    /// <summary>
    /// Counts requests by status and type. A requester gets their own, with their quotas and open requests. An
    /// administrator or API key gets everyone's, with each requester's, or one user's with <c>userId</c>.
    /// </summary>
    /// <returns>The counts.</returns>
    [HttpGet("/Requests/Stats")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<RequestStats> Stats()
    {
        if (GetAccess() is not { } access)
        {
            return Forbid();
        }

        var named = Guid.TryParse(Request.Query["userId"].ToString(), out var asked) && asked != Guid.Empty;
        // An administrator's own needs their userId; without one they get everyone's.
        return Ok(_requests.Stats(access.IsAdministrator && !named ? null : access));
    }

    /// <summary>Approves a pending or failed request, sending it to Radarr or Sonarr when set up.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="profileId">A profile to send it with instead of its own, before it has been sent.</param>
    /// <param name="profile">An alias of profileId.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated request, or not found.</returns>
    [HttpPost("/Requests/{requestId:guid}/Approve")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequestItemDto>> Approve([FromRoute(Name = "requestId")] Guid id, [FromQuery] string? profileId, [FromQuery] string? profile, CancellationToken cancellationToken)
    {
        profile = string.IsNullOrEmpty(profileId) ? profile : profileId;
        if (!string.IsNullOrEmpty(profile) && _db.Get(id) is { } existing)
        {
            var config = RequestConfig.Instance?.ReadConfiguration(c => c) ?? new RequestSettings();
            if (existing.ArrId is not null && profile != existing.ProfileId)
            {
                return BadRequest(new { Error = StolonTranslations.Get(TranslationKeys.RequestsErrorAlreadySentProfile, Language) });
            }

            if (!ArrServers.Usable(config, existing.Type).Any(u => u.Profile.Id == profile))
            {
                return BadRequest(new { Error = StolonTranslations.Get(TranslationKeys.RequestsErrorProfileGone, Language) });
            }
        }

        var request = await _requests.ApproveAsync(id, GetAccess(asCaller: true)?.UserName ?? "An administrator", cancellationToken, profileId: profile).ConfigureAwait(false);
        return request is null ? NotFound() : Ok(_items.For(request, null));
    }

    /// <summary>Lists the profiles requests can be sent with, each with its server, for choosing one on approval.</summary>
    /// <returns>The profiles, movies first, each type's default server first and the rest in their order.</returns>
    [HttpGet("/Requests/Profiles")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<ProfileChoice>> Profiles()
    {
        var config = RequestConfig.Instance?.ReadConfiguration(c => c) ?? new RequestSettings();
        return Ok(new[] { RequestType.Movie, RequestType.Series }
            .SelectMany(t => ArrServers.Usable(config, t).Select(u => new ProfileChoice(u.Profile.Id, u.Profile.Name, u.Server.Id, string.IsNullOrWhiteSpace(u.Server.Name) ? ArrClient.NameFor(t) : u.Server.Name, t)))
            .ToList());
    }

    /// <summary>
    /// Gives the page of a request's title in Radarr or Sonarr, at the server's browser address, for the queue's
    /// Open in Radarr or Sonarr link.
    /// </summary>
    /// <param name="id">The request id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The link, or no content when the title isn't there or its server isn't set up; not found for no such request.</returns>
    [HttpGet("/Requests/{requestId:guid}/ArrLink")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArrLink>> GetArrLink([FromRoute(Name = "requestId")] Guid id, CancellationToken cancellationToken)
    {
        if (_db.Get(id) is not { } request)
        {
            return NotFound();
        }

        return await _arr.LinkAsync(request, cancellationToken).ConfigureAwait(false) is { } link ? Ok(link) : NoContent();
    }

    /// <summary>Declines a request that is still open.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="body">The reason, if any.</param>
    /// <returns>The updated request, or not found.</returns>
    [HttpPost("/Requests/{requestId:guid}/Decline")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> Decline([FromRoute(Name = "requestId")] Guid id, [FromBody] DeclineRequest? body)
    {
        var request = _requests.Decline(id, GetAccess(asCaller: true)?.UserName ?? "An administrator", body?.Reason);
        return request is null ? NotFound() : Ok(_items.For(request, null));
    }

    /// <summary>Marks an open request available by hand, for a title the library checks missed.</summary>
    /// <param name="id">The request id.</param>
    /// <returns>The updated request, or not found.</returns>
    [HttpPost("/Requests/{requestId:guid}/Available")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RequestItemDto> MarkAvailable([FromRoute(Name = "requestId")] Guid id)
    {
        var request = _requests.MarkAvailable(id, GetAccess(asCaller: true)?.UserName ?? ServerWording.Get(TranslationKeys.RequestsValueAnAdministrator));
        return request is null ? NotFound() : Ok(_items.For(request, null));
    }

    /// <summary>Approves, declines or removes several requests at once.</summary>
    /// <param name="verb">approve, decline or remove. Not named "action", which ASP.NET reserves in routes.</param>
    /// <param name="body">The request ids, and a reason for a decline.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many it applied to.</returns>
    [HttpPost("/Requests/Bulk/{verb}")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkResult>> Bulk([FromRoute] string verb, [FromBody] BulkAction body, CancellationToken cancellationToken)
    {
        verb = (verb ?? string.Empty).ToLowerInvariant();
        if (body is null || verb is not ("approve" or "decline" or "remove"))
        {
            return BadRequest();
        }

        var admin = GetAccess(asCaller: true) ?? new UserAccess { UserName = "An administrator", IsAdministrator = true };
        return Ok(await _requests.BulkAsync(verb, body, admin, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Checks the library and Radarr and Sonarr for every open request now, rather than waiting for the tasks.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The counts after the check.</returns>
    [HttpPost("/Requests/Refresh")]
    [Authorize]
    [ManagesRequests]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestStats>> Refresh(CancellationToken cancellationToken)
    {
        await _requests.SweepAsync(null, cancellationToken).ConfigureAwait(false);

        // While the scheduled sync is running this waits for it to finish rather than starting a second one.
        await _requests.SyncProgressAsync(null, cancellationToken).ConfigureAwait(false);
        return Ok(_requests.Stats());
    }

    // What's wrong with the Radarr and Sonarr servers, or null, in the language given or US English for null. A server
    // without an address, key or complete profile would look set up while approvals send nothing.
    internal static string? ArrServersError(RequestSettings incoming, RequestSettings stored, string? language = null)
    {
        foreach (var server in (incoming.ArrServers ?? new()).Where(s => s is not null))
        {
            var name = string.IsNullOrWhiteSpace(server.Name) ? ArrClient.NameFor(server.Type) : server.Name.Trim();
            if (!ArrClient.TryNormalizeUrl(server.Url, out _))
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrAddressScheme, language, name);
            }

            // The browser address is only ever linked to, never sent the key, but a link has to be one a browser opens.
            if (!string.IsNullOrWhiteSpace(server.ExternalUrl) && !ArrClient.TryNormalizeUrl(server.ExternalUrl, out _))
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrExternalScheme, language, name);
            }

            if (string.IsNullOrWhiteSpace(server.ApiKey))
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrEnterKey, language, name);
            }

            // A saved key stays with the address it was saved for, so a moved server needs its key again.
            if (server.ApiKey == SecretProtector.KeptSentinel
                && !(stored.ArrServers ?? new()).Any(s => s?.Id == server.Id && ArrClient.SameAddress(s.Url, server.Url)))
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrEnterKeyAgain, language, name);
            }

            if (server.Profiles is not { Count: > 0 })
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrAddProfile, language, name);
            }

            if (server.Profiles.FirstOrDefault(p => p is null || p.QualityProfileId <= 0 || string.IsNullOrWhiteSpace(p.RootFolder)) is { } incomplete)
            {
                return StolonTranslations.Format(TranslationKeys.RequestsErrorArrChooseProfileFolder, language, string.IsNullOrWhiteSpace(incomplete?.Name) ? StolonTranslations.Get(TranslationKeys.RequestsValueEachProfile, language) : incomplete.Name, name);
            }
        }

        return null;
    }

    /// <summary>Blocks a title, or allows it again, from the request page.</summary>
    /// <param name="verb">block or unblock.</param>
    /// <param name="title">The title, by its ids.</param>
    /// <returns>No content, or bad request.</returns>
    [HttpPost("/Requests/Blocklist/{verb}")]
    [Authorize(Policy = AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult Block([FromRoute] string verb, [FromBody] BlockedTitle title)
    {
        verb = (verb ?? string.Empty).ToLowerInvariant();
        if (title is null || verb is not ("block" or "unblock") || !Enum.IsDefined(title.Type) || Blocklist.IdsOf(title).Count == 0)
        {
            return BadRequest();
        }

        title.Title = Cap(title.Title, 300);
        _requests.SetBlocked(title, verb == "block", GetAccess(asCaller: true)?.UserName ?? ServerWording.Get(TranslationKeys.RequestsValueAnAdministrator));
        return NoContent();
    }

    /// <summary>Tries a Radarr or Sonarr connection and lists its quality profiles and root folders.</summary>
    /// <param name="type">Movie for Radarr, Series for Sonarr.</param>
    /// <param name="body">The address and key to try; the kept sentinel uses the stored key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the server reported.</returns>
    [HttpPost("/Requests/Arr/{type}/Test")]
    [Authorize(Policy = AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ArrOptions>> TestArr([FromRoute] RequestType type, [FromBody] ArrTestRequest body, CancellationToken cancellationToken)
    {
        var plugin = RequestConfig.Instance;
        if (body is null || plugin is null || !Enum.IsDefined(type))
        {
            return BadRequest();
        }

        var (key, stored, error) = KeyToTest(body, type, plugin.ReadConfiguration(c => c), Language);
        if (error is not null)
        {
            return Ok(new ArrOptions { Error = error });
        }

        using var language = StolonTranslations.UseLanguage(Language);
        return Ok(await _arr.TestAsync(body.Url, stored ? plugin.Secrets.Unprotect(key) : key, cancellationToken).ConfigureAwait(false));
    }

    // The key a connection test uses: the one typed in, or for the kept sentinel the stored (still protected) key, but
    // only for the address it was saved with, so a stored key is never sent to an address an administrator just typed.
    internal static (string Key, bool Stored, string? Error) KeyToTest(ArrTestRequest body, RequestType type, RequestSettings stored, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(stored);
        if (!string.Equals(body.ApiKey, SecretProtector.KeptSentinel, StringComparison.Ordinal))
        {
            return (body.ApiKey, false, null);
        }

        var server = (stored.ArrServers ?? new()).FirstOrDefault(s => s is not null && s.Type == type && string.Equals(s.Id, body.ServerId, StringComparison.Ordinal));
        if (!ArrClient.TryNormalizeUrl(server?.Url, out var saved) || !ArrClient.TryNormalizeUrl(body.Url, out var asked)
            || !string.Equals(saved, asked, StringComparison.OrdinalIgnoreCase))
        {
            return (string.Empty, false, StolonTranslations.Get(TranslationKeys.RequestsErrorEnterKeyForAddress, language));
        }

        return (server!.ApiKey ?? string.Empty, true, null);
    }

    // A full copy with each secret swapped for the kept sentinel, so a setting added later round-trips
    // through the settings page without being listed here.
    internal static RequestSettings Redact(RequestSettings c)
    {
        static string Kept(string value) => SecretProtector.HasSecret(value) ? SecretProtector.KeptSentinel : string.Empty;
        var copy = Clone(c);
        foreach (var server in copy.ArrServers ?? new())
        {
            server.ApiKey = Kept(server.ApiKey);
        }

        copy.TmdbApiKey = Kept(copy.TmdbApiKey);

        return copy;
    }

    // What a requester sees of a request: Radarr and Sonarr stay out of it. Their ids, retry state and error
    // text are left out, and progress is told without naming them. Of someone else's request, the note is left
    // out, and who made it unless they may see that. Administrators see everything.
    internal static MediaRequest ForViewer(MediaRequest request, UserAccess viewer)
    {
        if (viewer.IsAdministrator)
        {
            // Older versions named the server in the progress text.
            if (Neutral(request.ProgressText) == request.ProgressText)
            {
                return request;
            }

            var own = Clone(request);
            own.ProgressText = Neutral(own.ProgressText);
            return own;
        }

        var copy = Clone(request);
        copy.ArrId = null;
        copy.SendFailures = 0;
        copy.RetryAfterUtc = null;
        copy.ExpectedEpisodes = null;
        copy.DecidedBy = copy.DecidedBy is not null && copy.DecidedBy.StartsWith("Auto-approved", StringComparison.Ordinal) ? ServerWording.Get(TranslationKeys.RequestsValueApprovedAutomatically) : copy.DecidedBy;
        // The only note written to the requester is why it was rejected; any other is what a server answered.
        if (copy.Status != RequestStatus.Declined)
        {
            copy.Note = null;
        }

        if (copy.UserId != viewer.UserId)
        {
            // Someone else's request: neither their note nor the reason it was rejected, which was written to them.
            // Without seeing requesters, one approved as it was made (by its requester's own trust) doesn't name them.
            copy.RequesterNote = null;
            copy.Note = null;
            if (!viewer.SeeRequesters)
            {
                // Decided by the hidden requester themselves: approved as it was made, or rejected by an administrator
                // who made it; either way their name stays out.
                if (string.Equals(copy.DecidedBy, copy.UserName, StringComparison.Ordinal))
                {
                    copy.DecidedBy = copy.Status == RequestStatus.Declined ? null : ServerWording.Get(TranslationKeys.RequestsValueApprovedAutomatically);
                }

                copy.UserId = Guid.Empty;
                copy.UserName = string.Empty;
            }
        }

        // Which server and profile it went to is the administrators' business; the requester keeps their own
        // request's profile, which they picked.
        copy.ServerId = null;

        // When administrators were reminded about it is theirs to know.
        copy.AgingNotifiedUtc = null;
        copy.ExpiringNotifiedDays = null;
        if (copy.UserId != viewer.UserId)
        {
            copy.ProfileId = null;
        }

        // Who else joined it, as who made it: named only to those who see requesters; the rest are counted.
        if (!viewer.SeeRequesters && copy.Joined is { } joined)
        {
            copy.Joined = joined.Select(j => j.UserId == viewer.UserId ? j : new RequestJoiner { JoinedUtc = j.JoinedUtc }).ToList();
        }

        copy.ProgressText = Neutral(copy.ProgressText);
        return copy;
    }

    // What a viewer sees of a request, as ForViewer leaves it, and without pointing at library items they may not see.
    private MediaRequest Shown(MediaRequest request, UserAccess viewer)
        => Shown(request, viewer, id => _requests.CanSee(id, viewer));

    private static MediaRequest Shown(MediaRequest request, UserAccess viewer, Func<Guid, bool> canSee)
        => viewer.IsAdministrator ? ForViewer(request, viewer) : HideUnseen(ForViewer(request, viewer), canSee);

    // A request for a title that's in a library the viewer can't see (an administrators' 4K library, say, or one
    // above their parental rating) doesn't point at it: neither its library item nor that item's artwork, whose
    // address would name the item and show what's there. Whose request it is doesn't matter: a requester who can't
    // see the copy that fulfilled theirs is no more allowed to see it than anyone else. The requester's own request
    // keeps its title and outside artwork, which say nothing about the library; lists of other people's leave such a
    // request out altogether. The copy given is changed, so it must not be a shared one.
    internal static MediaRequest HideUnseen(MediaRequest shown, Func<Guid, bool> canSee)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(canSee);
        if (shown.LibraryItemId is { } id && !canSee(id))
        {
            shown.LibraryItemId = null;
        }

        shown.PosterUrl = SeenImage(shown.PosterUrl, canSee);
        shown.BackdropUrl = SeenImage(shown.BackdropUrl, canSee);
        return shown;
    }

    private static string? SeenImage(string? url, Func<Guid, bool> canSee)
    {
        var item = url is null ? null : LibraryImageItem().Match(url);
        return item is { Success: true } && (!Guid.TryParse(item.Groups[1].Value, out var id) || !canSee(id)) ? null : url;
    }

    [GeneratedRegex(@"^\.\./Items/([0-9a-f]{32})/Images/")]
    private static partial Regex LibraryImageItem();

    // Download progress in words, without the server names. Every status text Radarr or Sonarr's side writes into
    // ProgressText must be matched here (or carry no server name), so a new one never shows a server to requesters.
    // The sentences that name a server are matched whole, so a server renamed or removed since the text was written is
    // left out too; Radarr and Sonarr elsewhere are swapped for "the downloader".
    internal static string? Neutral(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = SentTo().Replace(text, "Sent for download");
        text = NotInYet().Replace(text, "Not sent yet");
        text = NoLongerIn().Replace(text, "No longer downloading");
        // Written by older versions, which named the server.
        text = WillTryAnother().Replace(text, "Retrying download$1");
        return text.Replace("Radarr", "the downloader", StringComparison.Ordinal).Replace("Sonarr", "the downloader", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^Sent to .+$", RegexOptions.Singleline)]
    private static partial Regex SentTo();

    [GeneratedRegex(@"^Not in .+ yet$", RegexOptions.Singleline)]
    private static partial Regex NotInYet();

    [GeneratedRegex(@"^No longer in .+$", RegexOptions.Singleline)]
    private static partial Regex NoLongerIn();

    [GeneratedRegex(@"^Download failed, .+ will try another release( \(\d+ episodes?\))?$", RegexOptions.Singleline)]
    private static partial Regex WillTryAnother();

    private static string Cap(string? text, int max)
    {
        text = (text ?? string.Empty).Trim();
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>Keeps every answer, and any token in the page's address, out of referrers, caches and search indexes.
    /// The page's scripts, stylesheets, fonts, translations and logo then set their own caching. Anything under /issues/
    /// answers 404 while issues are off.</summary>
    /// <param name="context">The action about to run.</param>
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PublicPage.ApplyHardeningHeaders(context.HttpContext.Response);

        // The issue page and everything under it are not there while issues are off.
        if (context.HttpContext.Request.Path.StartsWithSegments("/issues", StringComparison.OrdinalIgnoreCase) && !IssueService.Enabled)
        {
            context.Result = NotFound();
            return;
        }

        // A route for managers takes administrators and members of a group that may manage, never anyone else.
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var forRequests = metadata.OfType<ManagesRequestsAttribute>().Any();
        var forIssues = metadata.OfType<ManagesIssuesAttribute>().Any();
        if (forRequests || forIssues)
        {
            var caller = GetAccess(asCaller: true);
            if (caller is null || (forRequests && !caller.IsAdministrator) || (forIssues && !caller.ManagesIssues))
            {
                context.Result = Forbid();
            }
        }
    }

    /// <inheritdoc />
    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    // Who the call is for, signed in as Jellyfin signs calls in (its token in the Authorization or X-Emby-Token header,
    // or api_key). An administrator, or an API key, may act for another user named by userId, as Jellyfin's own endpoints
    // take it; an API key without one is an administrator who is no user in particular. An administrator's own action
    // (asCaller) is always the caller's, so it's recorded under who did it.
    private UserAccess? GetAccess(bool asCaller = false)
    {
        var access = FindAccess(asCaller);
        if (access is not null)
        {
            access.Language = Language;
        }

        return access;
    }

    private UserAccess? FindAccess(bool asCaller)
    {
        var isApiKey = bool.TryParse(User.FindFirst(IsApiKeyClaim)?.Value, out var apiKey) && apiKey;
        var named = !asCaller && Guid.TryParse(Request.Query["userId"].ToString(), out var asked) && asked != Guid.Empty ? asked : (Guid?)null;
        if (isApiKey)
        {
            return named is { } forUser ? _requests.GetAccess(forUser) : ApiKeyAccess;
        }

        if (!Guid.TryParse(User.FindFirst(UserIdClaim)?.Value, out var userId) || _requests.GetAccess(userId) is not { } caller)
        {
            return null;
        }

        return named is { } other && other != caller.UserId && caller.IsAdministrator ? _requests.GetAccess(other) : caller;
    }

    // The page's language, for the messages it shows: the language query value, then the page's language header,
    // then the browser's.
    private string Language => StolonTranslations.RequestLanguage(Request);

    // A page asked for in Jellyfin's terms (startIndex, limit, searchTerm) or the plugin's own (skip, take, search,
    // filter), whichever the caller used.
    private static PagedQuery Paging(PagedQuery? query, int? startIndex, int? limit, string? searchTerm, string? status)
    {
        query ??= new PagedQuery();
        query.Skip = startIndex ?? query.Skip;
        query.Take = limit ?? query.Take;
        query.Search = searchTerm ?? query.Search;
        query.Filter = status ?? query.Filter;
        return query;
    }

    // One page of a list worked out in full, by the same rules as a page of requests.
    private static RequestsQueryResult<T> Slice<T>(IReadOnlyList<T> all, PagedQuery query)
    {
        var (skip, take) = RequestDatabase.Window(query);
        return new RequestsQueryResult<T>(skip, all.Count, all.Skip(skip).Take(take).ToList());
    }

    // Genres asked for, separated by | as Jellyfin's Genres are.
    private static List<string> GenreList(string? genres) => TitleFilter.GenreNames(genres).ToList();

    // How long an asset may be kept: asked for with the current version (?v=, as the page links it), it never changes
    // at that address, so it's kept for good. An older version gets what's current now, so that isn't kept at all;
    // without one, for the time given.
    private void CacheAsset(TimeSpan otherwise)
        => Response.Headers["Cache-Control"] = !Request.Query.TryGetValue("v", out var asked)
            ? "public, max-age=" + ((int)otherwise.TotalSeconds).ToString(CultureInfo.InvariantCulture)
            : string.Equals(asked.ToString(), CurrentVersion(), StringComparison.Ordinal) ? "public, max-age=31536000, immutable" : "no-cache";

    // A page with its items turned into others, keeping where it starts and how many there are in all.
    private static RequestsQueryResult<TOut> Map<TIn, TOut>(RequestsQueryResult<TIn> page, Func<TIn, TOut> into)
        => new(page.StartIndex, page.TotalRecordCount, page.Items.Select(into).ToList());

    // The same, with the page's items turned into others all at once, in their order.
    private static RequestsQueryResult<TOut> MapAll<TIn, TOut>(RequestsQueryResult<TIn> page, Func<IReadOnlyList<TIn>, List<TOut>> into)
        => new(page.StartIndex, page.TotalRecordCount, into(page.Items.ToList()));

    // A request in Jellyfin's item format, as the viewer may see it.
    private RequestItemDto Item(MediaRequest request, UserAccess access) => _items.For(Shown(request, access), access.User);

    // A page of requests in Jellyfin's item format, as the viewer may see them, with where the next page starts (null
    // after the last). Their library items are read together and built in one go, and who may see each is decided once.
    private RequestsQueryResult<RequestItemDto> Items(IEnumerable<MediaRequest> requests, int total, PagedQuery query, UserAccess access, Sight? sight = null)
    {
        var skip = RequestDatabase.Window(query).Skip;
        var page = requests.ToList();
        sight ??= SightOf(page, access);
        var items = _items.For(page.Select(r => Shown(r, access, sight.CanSee)).ToList(), access.User, sight.Loaded);
        return new(skip, total, items) { NextStartIndex = skip + items.Count < total ? skip + items.Count : null };
    }

    // A page of requests with those the viewer may not see left out. NextStartIndex says where the next page starts in
    // the database's order, so leaving rows out never repeats or skips any. The count can't say how many were left
    // out, so it counts only what has been shown so far, and one more while there may be more.
    private RequestsQueryResult<RequestItemDto> Visible(PagedResult<MediaRequest> page, Func<MediaRequest, bool> seen, PagedQuery query, UserAccess access, Sight? sight = null)
    {
        var skip = RequestDatabase.Window(query).Skip;
        var shown = page.Items.Where(seen).ToList();
        int? next = skip + page.Items.Count < page.TotalCount ? skip + page.Items.Count : null;
        var result = Items(shown, access.IsAdministrator ? page.TotalCount : skip + shown.Count + (next is null ? 0 : 1), query, access, sight);
        result.NextStartIndex = next;
        return result;
    }

    // Who may see what for a page of requests: every library item it names (a request's own, and those its poster and
    // backdrop are from) read in one query.
    private Sight SightOf(IEnumerable<MediaRequest> requests, UserAccess access)
        => new(_requests, access, _items.Load(requests.SelectMany(LibraryItemsNamed)));

    // The library items a request points at: its own, and those its artwork's addresses name.
    private static IEnumerable<Guid> LibraryItemsNamed(MediaRequest request)
    {
        if (request.LibraryItemId is { } id)
        {
            yield return id;
        }

        foreach (var url in new[] { request.PosterUrl, request.BackdropUrl })
        {
            if (url is not null && LibraryImageItem().Match(url) is { Success: true } named && Guid.TryParse(named.Groups[1].Value, out var imageItem))
            {
                yield return imageItem;
            }
        }
    }

    /// <summary>
    /// Who may see which library items, for one answer. A page asks about each of its requests' items up to four times
    /// (whether to list the request, its item, its poster, its backdrop), so each is decided once, from the items read
    /// together for the page, as <see cref="RequestService.CanSee"/> decides it.
    /// </summary>
    private sealed class Sight
    {
        private readonly RequestService _requests;
        private readonly UserAccess _access;
        private readonly Dictionary<Guid, bool> _decided = new();

        public Sight(RequestService requests, UserAccess access, Dictionary<Guid, MediaBrowser.Controller.Entities.BaseItem> loaded)
        {
            _requests = requests;
            _access = access;
            Loaded = loaded;
        }

        /// <summary>Gets the library items read for the page, by id.</summary>
        public Dictionary<Guid, MediaBrowser.Controller.Entities.BaseItem> Loaded { get; }

        /// <summary>Returns whether the viewer may see a library item.</summary>
        /// <param name="id">The item's id.</param>
        /// <returns>True when they may.</returns>
        public bool CanSee(Guid id)
        {
            if (_access.IsAdministrator)
            {
                return true;
            }

            if (_decided.TryGetValue(id, out var known))
            {
                return known;
            }

            // An item not read with the page (gone, or never there) is asked about on its own, which reads it again.
            var seen = Loaded.TryGetValue(id, out var item)
                ? _access.User is not null && LibraryMatcher.IsVisibleTo(item, _access.User)
                : _requests.CanSee(id, _access);
            _decided[id] = seen;
            return seen;
        }
    }

    // An API key without a user: everything an administrator may do, but nothing of its own to request or list.
    private static UserAccess ApiKeyAccess => new()
    {
        UserName = "API key",
        IsAdministrator = true,
        CanRequest = true,
        Movies = true,
        Series = true,
        SeeAllRequests = true,
        SeeRequesters = true,
        AutoApproveMovies = true,
        AutoApproveShows = true
    };

    private sealed record BuiltPage(string Version, string Html, string Template, string ServerId, PackedAsset Script, PackedAsset Style);

    /// <summary>
    /// A script or stylesheet as served: its UTF-8 bytes, and the same gzipped, made once when the page is built rather
    /// than on every request for it.
    /// </summary>
    internal sealed class PackedAsset
    {
        private PackedAsset(byte[] bytes, byte[] gzipped)
        {
            Bytes = bytes;
            Gzipped = gzipped;
        }

        /// <summary>Gets the text's UTF-8 bytes.</summary>
        public byte[] Bytes { get; }

        /// <summary>Gets the same bytes gzipped.</summary>
        public byte[] Gzipped { get; }

        /// <summary>Packs a text.</summary>
        /// <param name="text">The script or stylesheet.</param>
        /// <returns>It packed.</returns>
        public static PackedAsset Of(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            using var packed = new MemoryStream();
            using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(bytes);
            }

            return new PackedAsset(bytes, packed.ToArray());
        }
    }

    /// <summary>
    /// The assets' fingerprint, kept with what it was worked out from: the stylesheets as cached, the page's script
    /// and the build. Those are kept strings, the same ones until one is looked up again, so they are compared by
    /// reference, which costs nothing, and the hash over them all (tens of kilobytes) is worked out again only when one
    /// of them has been replaced.
    /// </summary>
    internal sealed class AssetFingerprint
    {
        private Worked? _last;

        /// <summary>Returns the fingerprint of these, worked out again only when one isn't the one last given.</summary>
        /// <param name="parts">What the fingerprint is of.</param>
        /// <returns>The fingerprint.</returns>
        public string Of(params string[] parts)
        {
            ArgumentNullException.ThrowIfNull(parts);
            var last = _last;
            if (last is not null && last.Parts.Length == parts.Length && !parts.Where((p, i) => !ReferenceEquals(p, last.Parts[i])).Any())
            {
                return last.Version;
            }

            var version = AssetVersion(parts);
            _last = new Worked(parts, version);
            return version;
        }

        private sealed record Worked(string[] Parts, string Version);
    }

    // A deep copy through JSON, so changing it leaves the original (the stored configuration, a cached request) alone.
    private static T Clone<T>(T value)
        where T : class, new()
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? new T();
}
