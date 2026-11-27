using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Services.Groups;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// Serves the custom pages at their own addresses under <c>/pages</c>, with their images, server side routes, and
/// stores. A Signed in page opens for administrators and the groups that give it.
/// </summary>
[ApiController]
[Route("pages")]
public class CustomPagesController : ControllerBase
{
    private const string AdminPolicy = "RequiresElevation";

    private const string HtmlContentType = "text/html; charset=utf-8";

    // ONE policy for every served page. The anonymous path and the auth shell must not diverge: a
    // srcdoc frame inherits its embedder's policy, so whatever is withheld here is withheld from author
    // content, and two policies would make the same page behave differently depending on its tier.
    //
    // The boundary that actually protects the viewer is the sandboxed, opaque-origin iframe in
    // PageService.Render, not this header. Content that escaped that sandbox could exfiltrate by simply
    // navigating, which no CSP directive governs, so tightening the fetch directives buys almost no
    // security while silently breaking ordinary pages (CDN scripts, web fonts, embeds, form posts).
    // The fetch directives are therefore permissive, and the directives that protect the framing
    // document itself stay locked down:
    //   object-src 'none'    - no legacy plugin content.
    //   base-uri 'none'      - a <base> tag cannot repoint the wrapper's relative URLs, which is what
    //                          keeps author `asset/{name}` references resolving to /pages/asset/{name}.
    //   frame-ancestors 'self' - embeddable by the Jellyfin origin (a dashboard or another page), not
    //                          by third-party sites.
    // form-action is deliberately unset, so pages may post forms; it is omitted rather than set to a
    // value so both paths agree by construction.
    private const string PageCsp =
        "default-src * data: blob: 'unsafe-inline' 'unsafe-eval'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'self'";

    // The largest store request body. The record cap governs the payload; the rest of the allowance
    // covers the envelope around it.
    private const int StoreBodyLimit = StoreService.MaxRecordBytes + 4096;

    // Store responses are serialized here rather than through the server's MVC options, so a page
    // author gets the same camel cased shape no matter how the host has its own API configured.
    private static readonly JsonSerializerOptions StoreResponseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IPageService _pages;
    private readonly IStoreService _stores;
    private readonly IAuthorizationContext _authorization;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerApplicationPaths _paths;
    private readonly PermissionService _permissions;
    private readonly SecretProtector _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomPagesController"/> class.
    /// </summary>
    /// <param name="pages">Finds and renders the pages.</param>
    /// <param name="stores">Holds the store records.</param>
    /// <param name="authorization">Finds the calling user.</param>
    /// <param name="httpClientFactory">Forwards the server side routes.</param>
    /// <param name="paths">Locates the web client's favicon.</param>
    /// <param name="permissions">Gives what the calling user's groups allow.</param>
    /// <param name="secrets">Reads route passwords back.</param>
    public CustomPagesController(
        IPageService pages,
        IStoreService stores,
        IAuthorizationContext authorization,
        IHttpClientFactory httpClientFactory,
        IServerApplicationPaths paths,
        PermissionService permissions,
        SecretProtector secrets)
    {
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _stores = stores ?? throw new ArgumentNullException(nameof(stores));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _paths = paths;
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>
    /// Entry point for a page. Anonymous pages render directly; protected pages render an auth shell
    /// that re-fetches the content with the signed-in user's token.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page or shell HTML.</returns>
    [HttpGet("{slug}")]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ContentResult Get(string slug)
    {
        Harden();

        var page = _pages.Find(slug);
        if (page is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Content(_pages.NotFoundHtml(slug, StolonTranslations.RequestLanguage(Request)), HtmlContentType);
        }

        if (!page.Visibility.RequiresAuth())
        {
            return Content(_pages.Render(page), HtmlContentType);
        }

        return Content(_pages.GetShellHtml(page.Slug, page.Visibility, StolonTranslations.RequestLanguage(Request)), HtmlContentType);
    }

    /// <summary>
    /// Serves the web client's favicon so pages can reuse the server's real icon at a stable path.
    /// </summary>
    /// <returns>The favicon bytes, or 404 when none could be located.</returns>
    [HttpGet("favicon.ico")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Favicon() => PublicPage.Favicon(_paths, Response);

    /// <summary>
    /// Serves a hosted anonymous image asset by name. Referenced from pages as <c>asset/{name}</c>.
    /// </summary>
    /// <param name="name">The asset name.</param>
    /// <returns>The asset bytes, or 404 when no anonymous asset matches.</returns>
    [HttpGet("asset/{name}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Asset(string name)
    {
        // Browsers fetch images without the viewer's token, so this endpoint can only ever serve
        // anonymous assets. Gated assets are embedded into their pages as data: URIs on render and
        // return 404 here so the public endpoint does not disclose which gated names exist.
        var asset = _pages.FindAsset(name);
        if (asset is null || asset.Visibility.RequiresAuth())
        {
            return NotFound();
        }

        var bytes = _pages.GetAssetBytes(asset);
        if (bytes is null)
        {
            return NotFound();
        }

        var contentType = asset.ContentType is not null
            && asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? asset.ContentType
                : "application/octet-stream";

        // Assets render fine as <img>/CSS backgrounds, but a script-bearing format (e.g. SVG) opened as a
        // top-level document would otherwise run same-origin. The sandbox CSP neutralizes that without
        // affecting embedded image rendering.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Public assets are cached by browsers and by any shared cache in front of the server. Raising an
        // asset's tier therefore does not retract copies already handed out: expect it to stay fetchable
        // for up to this window. Anything that must be retracted immediately should be deleted, not
        // re-tiered.
        Response.Headers["Cache-Control"] = "public, max-age=300";
        return File(bytes, contentType);
    }

    /// <summary>
    /// Returns the rendered content of a user-tier page to any signed-in user.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page HTML.</returns>
    [HttpGet("{slug}/user")]
    [Authorize]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UserContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.User)
        {
            return NotFound();
        }

        if (!await AllowsCallerAsync(page).ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Harden();
        return Content(_pages.Render(page), HtmlContentType);
    }

    /// <summary>
    /// Returns the rendered content of an admin-tier page to administrators.
    /// </summary>
    /// <param name="slug">The page slug.</param>
    /// <returns>The page HTML.</returns>
    [HttpGet("{slug}/admin")]
    [Authorize(Policy = AdminPolicy)]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AdminContent(string slug)
    {
        var page = _pages.Find(slug);
        if (page is null || page.Visibility != PageVisibility.Admin)
        {
            return NotFound();
        }

        Harden();
        return Content(_pages.Render(page), HtmlContentType);
    }

    /// <summary>
    /// Finds the calling user and asks whether their groups give a Signed in page.
    /// </summary>
    /// <remarks>
    /// Both the tier and this run before <see cref="IPageService.Render"/>, so a refused viewer is never sent the
    /// page. A caller that cannot be found counts as no user. An API key carries no user and is in no group, so it is
    /// refused too.
    /// </remarks>
    /// <param name="page">The page asked for.</param>
    /// <returns><c>true</c> when the caller may open the page.</returns>
    private async Task<bool> AllowsCallerAsync(CustomPage page)
    {
        AuthorizationInfo info;
        try
        {
            info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or SecurityException)
        {
            return false;
        }

        return PageAccess.MayOpen(page, CallerPermissions(info));
    }

    /// <summary>
    /// Works out what a caller's groups allow, or nothing for a caller with no user, such as an API key.
    /// </summary>
    /// <param name="info">The caller's authorization.</param>
    /// <returns>The caller's permissions, or <c>null</c>.</returns>
    private EffectivePermissions? CallerPermissions(AuthorizationInfo info)
        => info.IsApiKey || info.User is null || info.UserId == Guid.Empty ? null : _permissions.For(info.User);

    /// <summary>
    /// Forwards a page's named server side route to its configured target. The page calls this through the
    /// injected <c>serverFetch</c> helper, so a browser never faces the target directly and the target's
    /// credentials stay on the server.
    /// </summary>
    /// <remarks>
    /// The route target is fixed in configuration and never comes from the caller, so this is not an open
    /// proxy: a page can only reach the resources an administrator wired up for it. Access is gated to
    /// exactly the audience of the page the route belongs to, checked before anything is forwarded.
    /// </remarks>
    /// <param name="slug">The page slug.</param>
    /// <param name="name">The route name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The target's response, 403/404 when the caller or route is not permitted, 413 for an
    /// oversized body, or 502/504 when the target could not answer.</returns>
    [HttpGet("{slug}/api/{name}")]
    [HttpPost("{slug}/api/{name}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> ApiRoute(string slug, string name, CancellationToken cancellationToken)
    {
        var page = _pages.Find(slug);
        var route = page?.ApiRoutes?.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (page is null || route is null)
        {
            return NotFound();
        }

        var access = await CheckPageAccessAsync(page).ConfigureAwait(false);
        if (access != StatusCodes.Status200OK)
        {
            return StatusCode(access);
        }

        byte[]? body = null;
        if (Request.ContentLength is > 0 || HttpMethods.IsPost(Request.Method))
        {
            // A route on an Anyone page is reachable without signing in, so the body is capped as it is
            // read rather than buffered whole and measured afterwards.
            var (read, tooLarge) = await ReadBodyAsync(RouteProxy.MaxRequestBytes, cancellationToken).ConfigureAwait(false);
            if (tooLarge)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            body = read;
        }

        var requestHeaders = Request.Headers
            .Select(h => new KeyValuePair<string, string>(h.Key, h.Value.ToString()));

        var forwarded = await RouteProxy.ForwardAsync(
            _httpClientFactory,
            route,
            Request.Method,
            body,
            Request.ContentType,
            requestHeaders,
            _secrets.Unprotect(route.Password),
            cancellationToken).ConfigureAwait(false);

        if (forwarded is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }

        // A target that could not answer becomes a gateway status rather than an unhandled exception.
        if (forwarded.Error != ForwardError.None)
        {
            Response.Headers["Cache-Control"] = "no-store";
            return StatusCode(forwarded.StatusCode);
        }

        // The relay is an allow list, so the target cannot set cookies, CORS, or framing policy on the
        // Jellyfin origin. The headers this endpoint sets itself are applied after it regardless, so they
        // win even if the allow list ever grows to include one of them.
        foreach (var header in forwarded.Headers)
        {
            Response.Headers[header.Key] = header.Value;
        }

        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // The response is served from the Jellyfin origin, so a target answering with HTML would run
        // script there if somebody navigated to the route directly. The sandbox directive puts such a
        // document on an opaque origin with scripts off. It is applied only to types a browser renders
        // as a scriptable document, since a sandboxed PDF cannot open in the browser's viewer. A page
        // reading the route through fetch is unaffected either way, since a response's policy only
        // applies when it is rendered as a document.
        if (RouteProxy.NeedsSandbox(forwarded.ContentType))
        {
            Response.Headers["Content-Security-Policy"] = "sandbox";
        }

        Response.StatusCode = forwarded.StatusCode;
        return File(forwarded.Body, forwarded.ContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// Returns the records in a store the caller is allowed to see, newest first.
    /// </summary>
    /// <remarks>
    /// Stores are addressed by name rather than through a page, so which page is calling has no bearing
    /// on what comes back. The store's own read tier and read scope decide everything, which is what
    /// lets a page that collects submissions and a page that reports on them work against the same
    /// records without one of them silently widening the other's audience.
    /// </remarks>
    /// <param name="name">The store name.</param>
    /// <param name="id">An optional single record ID to fetch instead of the whole store.</param>
    /// <returns>The records, or the status refusing the caller.</returns>
    [HttpGet("store/{name}/read")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreRead(string name, [FromQuery] string? id)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        var level = StoreAccess.ResolveRead(store, caller);
        if (level == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var owner = StoreAccess.OwnerFilter(level, caller);
        NoStore();

        if (!string.IsNullOrEmpty(id))
        {
            var single = _stores.ReadOne(store, id, owner);
            return single is null ? NotFound() : Json(single);
        }

        var records = _stores.Read(store, owner);
        return Json(new StoreReadResponse
        {
            Records = records,
            Count = records.Count,
            Limit = StoreService.EffectiveMaxRecords(store),
            Scope = level == StoreAccessLevel.All ? "all" : "own"
        });
    }

    /// <summary>
    /// Creates a record, or replaces the payload of one the caller may modify.
    /// </summary>
    /// <remarks>
    /// A body carrying no <c>id</c> creates a record and the server assigns its ID, timestamps, and
    /// owner. A body carrying an <c>id</c> replaces that record's payload and leaves the rest alone, so
    /// an administrator writing a status onto a submission never takes ownership of it away from the
    /// person who submitted it.
    /// </remarks>
    /// <param name="name">The store name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored record, or the status refusing the write.</returns>
    [HttpPost("store/{name}/write")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status507InsufficientStorage)]
    public async Task<IActionResult> StoreWrite(string name, CancellationToken cancellationToken)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        if (!StoreAccess.CanCreate(store, caller))
        {
            return Refuse(caller);
        }

        var (body, tooLarge) = await ReadBodyAsync(StoreBodyLimit, cancellationToken).ConfigureAwait(false);
        if (tooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        JsonNode? envelope;
        try
        {
            envelope = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var recordId = ReadStringProperty(envelope, "id");
        var data = ReadProperty(envelope, "data");

        // The record's primary element. It only ever names the record in the activity log, so it is
        // never interpreted and the store caps and flattens it before it reaches an administrator.
        var label = ReadStringProperty(envelope, "label");

        NoStore();

        if (string.IsNullOrEmpty(recordId))
        {
            var created = _stores.Create(store, data, label, caller.UserId);
            return StoreResult(created);
        }

        // Updating an existing record is a different grant from creating one, so it is resolved
        // separately rather than inferred from the caller having got this far.
        var modify = StoreAccess.ResolveModify(store, caller);
        if (modify == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var updated = _stores.Update(store, recordId, data, label, StoreAccess.OwnerFilter(modify, caller));
        return StoreResult(updated);
    }

    /// <summary>
    /// Deletes one record the caller may modify.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>204 when a record was deleted, otherwise the refusing status.</returns>
    [HttpPost("store/{name}/delete")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreDelete(string name, CancellationToken cancellationToken)
    {
        var store = _stores.Find(name);
        if (store is null)
        {
            return NotFound();
        }

        var caller = await ResolveStoreCallerAsync().ConfigureAwait(false);
        var modify = StoreAccess.ResolveModify(store, caller);
        if (modify == StoreAccessLevel.None)
        {
            return Refuse(caller);
        }

        var (body, tooLarge) = await ReadBodyAsync(StoreBodyLimit, cancellationToken).ConfigureAwait(false);
        if (tooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        JsonNode? envelope;
        try
        {
            envelope = body.Length == 0 ? null : JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var recordId = ReadStringProperty(envelope, "id");
        if (string.IsNullOrEmpty(recordId))
        {
            return BadRequest();
        }

        NoStore();
        return _stores.Delete(store, recordId, StoreAccess.OwnerFilter(modify, caller))
            ? NoContent()
            : NotFound();
    }

    /// <summary>
    /// Resolves the identity behind a store request.
    /// </summary>
    /// <remarks>
    /// An API key is treated as the administrator tier with no user identity, which is deliberately
    /// different from how the page endpoints treat one. A Jellyfin API key is issued by an
    /// administrator and already carries administrator reach, and a store's whole point is that
    /// something outside the browser can pick work up and write results back. Carrying no user means it
    /// can never own a record, so the Own read scope and the edit-own grant never apply to it. An API
    /// key therefore sees a store exactly as an administrator does, and nothing narrower is silently
    /// widened for it.
    /// </remarks>
    /// <returns>The resolved caller.</returns>
    private async Task<StoreCaller> ResolveStoreCallerAsync()
    {
        AuthorizationInfo info;
        try
        {
            info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or SecurityException)
        {
            // A malformed or revoked token is not a credential, so fall back to the anonymous tier
            // rather than failing the request outright. An anonymous store still answers it.
            return StoreCaller.Anonymous;
        }

        if (info.IsApiKey)
        {
            return new StoreCaller(PageVisibility.Admin, Guid.Empty);
        }

        if (info.User is null || info.UserId == Guid.Empty)
        {
            return StoreCaller.Anonymous;
        }

        var tier = info.User.HasPermission(PermissionKind.IsAdministrator)
            ? PageVisibility.Admin
            : PageVisibility.User;

        return new StoreCaller(tier, info.UserId);
    }

    /// <summary>
    /// Reads the request body, refusing anything past <paramref name="limit"/> so an oversized payload is
    /// turned away before it is parsed or forwarded rather than after.
    /// </summary>
    /// <param name="limit">The largest body accepted, in bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body bytes, and whether the limit was exceeded.</returns>
    private async Task<(byte[] Body, bool TooLarge)> ReadBodyAsync(long limit, CancellationToken cancellationToken)
    {
        // An honest client declares its length, so an oversized body is refused without reading any of it.
        if (Request.ContentLength > limit)
        {
            return (Array.Empty<byte>(), true);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return (Array.Empty<byte>(), true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return (buffer.ToArray(), false);
    }

    /// <summary>
    /// Maps a store write outcome onto the response the caller sees.
    /// </summary>
    /// <param name="outcome">The store's outcome.</param>
    /// <returns>The response.</returns>
    private IActionResult StoreResult((StoreWriteResult Result, StoreRecord? Record) outcome) => outcome.Result switch
    {
        StoreWriteResult.Ok when outcome.Record is not null => Json(outcome.Record),
        StoreWriteResult.NotFound => NotFound(),
        StoreWriteResult.Full => StatusCode(StatusCodes.Status409Conflict),
        StoreWriteResult.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge),
        StoreWriteResult.OverBudget => StatusCode(StatusCodes.Status507InsufficientStorage),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    /// <summary>
    /// Returns the status for a refused store request. A caller with no credentials is told to sign in,
    /// and one that is signed in and still short of the tier is refused outright.
    /// </summary>
    /// <param name="caller">The resolved caller.</param>
    /// <returns>401 or 403.</returns>
    private StatusCodeResult Refuse(StoreCaller caller) => StatusCode(
        caller.Tier == PageVisibility.Anonymous
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden);

    /// <summary>
    /// Serializes a store response with the plugin's own options, so a page author sees the same camel
    /// cased shape regardless of how the server has its own API serialization configured.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The JSON response.</returns>
    private ContentResult Json(object value)
        => Content(JsonSerializer.Serialize(value, StoreResponseJson), "application/json; charset=utf-8");

    /// <summary>
    /// Keeps store responses out of caches. Records change constantly and are frequently gated, so a
    /// shared cache in front of the server must never hold one.
    /// </summary>
    private void NoStore()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static JsonNode? ReadProperty(JsonNode? envelope, string name)
    {
        if (envelope is not JsonObject obj)
        {
            return null;
        }

        foreach (var property in obj)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadStringProperty(JsonNode? envelope, string name)
        => ReadProperty(envelope, name) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    /// <summary>
    /// Works out whether the caller may reach a page's audience, by the same rule the content routes use.
    /// </summary>
    /// <param name="page">The page whose audience gates the route.</param>
    /// <returns>200 when allowed, 401 when a signed in user is needed, or 403 when refused.</returns>
    private async Task<int> CheckPageAccessAsync(CustomPage page)
    {
        if (!page.Visibility.RequiresAuth())
        {
            return StatusCodes.Status200OK;
        }

        AuthorizationInfo info;
        try
        {
            info = await _authorization.GetAuthorizationInfo(Request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AuthenticationException or SecurityException)
        {
            // A malformed or revoked token is no credential, so the caller is told to sign in again.
            return StatusCodes.Status401Unauthorized;
        }

        if (info.IsApiKey || info.User is null || info.UserId == Guid.Empty)
        {
            return StatusCodes.Status401Unauthorized;
        }

        return PageAccess.MayOpen(page, _permissions.For(info.User))
            ? StatusCodes.Status200OK
            : StatusCodes.Status403Forbidden;
    }

    /// <summary>
    /// Applies the response headers that keep served pages out of caches, indexes, and third-party frames.
    /// </summary>
    private void Harden()
    {
        // Referrer-Policy, Cache-Control: no-store, and X-Robots-Tag come from the shared standalone page set.
        PublicPage.ApplyHardeningHeaders(Response);
        Response.Headers["Content-Security-Policy"] = PageCsp;
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Legacy counterpart to frame-ancestors 'self'; browsers that honour both prefer the CSP.
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    }
}
