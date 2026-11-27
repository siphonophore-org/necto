using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Invites;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Invites;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// The routes of Invites: the Invites tool's list and changes, a member's own invites, and the signup page an invite
/// link opens, which anyone with the link may use.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Invites")]
[Produces(MediaTypeNames.Application.Json)]
public class InvitesController : ControllerBase
{
    /// <summary>The page an invite link opens.</summary>
    public static readonly HostedPage SignupPage = new("necto", HostedPageAccess.Anyone, HostedPageContent.Resource(typeof(Plugin).Assembly, "Web.necto_invite_signup.html"))
    {
        Translations = "Necto/Translations",
        Mark = "Invites",
        MarkIcon = "mail"
    };

    /// <summary>The page where a member makes and shares their own invites.</summary>
    public static readonly HostedPage MemberPage = new("necto", HostedPageAccess.User, HostedPageContent.Resource(typeof(Plugin).Assembly, "Web.necto_invites_member.html"))
    {
        Translations = "Necto/Translations",
        Mark = "Invites",
        MarkIcon = "mail"
    };

    private readonly InviteService _invites;
    private readonly InviteAttemptLimiter _limiter;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly ILogger<InvitesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitesController"/> class.
    /// </summary>
    /// <param name="invites">Makes, changes, and uses invites.</param>
    /// <param name="limiter">Limits the signup page's calls by address.</param>
    /// <param name="store">Reads the settings and the groups.</param>
    /// <param name="userManager">Finds the caller.</param>
    /// <param name="logger">Writes the server log.</param>
    public InvitesController(InviteService invites, InviteAttemptLimiter limiter, IConfigurationStore store, IUserManager userManager, ILogger<InvitesController> logger)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists every invite, oldest first. It is not at <c>/Invites</c>, since that address is a member's invite page.
    /// </summary>
    /// <returns>The invites.</returns>
    [HttpGet("List")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<InviteSummary>> List() => Ok(_invites.Summaries());

    /// <summary>
    /// Makes an invite.
    /// </summary>
    /// <param name="request">What it holds.</param>
    /// <returns>The invite, or 400 with the reason.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<InviteSummary> Create([FromBody] InviteRequest request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var (invite, error) = _invites.Create(request, Guid.Empty, StolonTranslations.RequestLanguage(Request));
        return error is null ? Ok(invite) : BadRequest(new ApiError(error));
    }

    /// <summary>
    /// Changes an invite. A PIN left out stays as it is, and an empty one removes it.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="request">What it holds.</param>
    /// <returns>The invite, 400 with the reason, or 404.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<InviteSummary> Update([FromRoute] Guid id, [FromBody] InviteRequest request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var (invite, error) = _invites.Update(id, request, StolonTranslations.RequestLanguage(Request));
        if (error is not null)
        {
            return BadRequest(new ApiError(error));
        }

        return invite is null ? NotFound() : Ok(invite);
    }

    /// <summary>
    /// Makes a new invite with another's settings and a new link.
    /// </summary>
    /// <param name="id">The invite to copy.</param>
    /// <returns>The copy, or 404.</returns>
    [HttpPost("{id:guid}/Duplicate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<InviteSummary> Duplicate([FromRoute] Guid id) => _invites.Duplicate(id) is { } copy ? Ok(copy) : NotFound();

    /// <summary>
    /// Deletes an invite.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <returns>204, or 404.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult Delete([FromRoute] Guid id) => _invites.Delete(id) ? NoContent() : NotFound();

    /// <summary>
    /// Turns an invite on or off. Turning it on clears its wrong PINs.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="request">Whether it works.</param>
    /// <returns>204, 400 with the reason it stays off, or 404.</returns>
    [HttpPost("{id:guid}/Enabled")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SetEnabled([FromRoute] Guid id, [FromBody] InviteEnabledRequest request)
    {
        var language = StolonTranslations.RequestLanguage(Request);
        return _invites.SetEnabled(id, request?.Enabled ?? false) switch
        {
            InviteToggleResult.Updated => NoContent(),
            InviteToggleResult.Expired => BadRequest(new ApiError(StolonTranslations.Get(TranslationKeys.InvitesErrorExpiredEnable, language))),
            InviteToggleResult.GroupBlocksInvites => BadRequest(new ApiError(StolonTranslations.Get(TranslationKeys.InvitesErrorGroupBlocks, language))),
            _ => NotFound()
        };
    }

    /// <summary>
    /// Moves an invite's day. A day ahead, or none, turns it back on.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <param name="request">The new day.</param>
    /// <returns>204, or 404.</returns>
    [HttpPost("{id:guid}/Expiry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SetExpiry([FromRoute] Guid id, [FromBody] InviteExpiryRequest request)
        => _invites.SetExpiry(id, request?.ExpiresAt) ? NoContent() : NotFound();

    /// <summary>
    /// Serves the page where a signed in member makes and shares their invites.
    /// </summary>
    /// <returns>The page.</returns>
    [HttpGet("/invites")]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ContentResult MemberInvites() => MemberPage.Serve(this);

    /// <summary>
    /// Serves the signup page an invite link opens. Its script reads the invite and makes the account.
    /// </summary>
    /// <param name="token">The token in the link.</param>
    /// <returns>The page.</returns>
    [HttpGet("/invites/{token}")]
    [AllowAnonymous]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ContentResult Signup([FromRoute] string token) => SignupPage.Serve(this);

    /// <summary>
    /// Says whether an invite can be used, without saying why when it cannot. The invite's name stays private.
    /// </summary>
    /// <param name="token">The token in the link.</param>
    /// <returns>Whether it works, its welcome message, and whether it asks for a PIN, or 429 past the limit.</returns>
    [HttpGet("/invites/{token}/Info")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<InviteInfoView> Info([FromRoute] string token)
    {
        PublicPage.ApplyHardeningHeaders(Response);
        if (Throttled(InviteEndpoint.Info) is { } throttled)
        {
            return throttled;
        }

        var invite = _invites.FindByToken(token);
        if (invite is null || !_invites.IsRedeemableNow(invite))
        {
            return Ok(new InviteInfoView(false, string.Empty, false, false, string.Empty));
        }

        return Ok(new InviteInfoView(true, invite.Message, !string.IsNullOrEmpty(invite.PinHash), false, string.Empty));
    }

    /// <summary>
    /// Uses an invite to make an account.
    /// </summary>
    /// <param name="token">The token in the link.</param>
    /// <param name="request">The PIN, user name, and password.</param>
    /// <param name="cancellationToken">Stops the wait for another signup.</param>
    /// <returns>Whether the account was made, a sentence, and the links to show, or 429 past the limit.</returns>
    [HttpPost("/invites/{token}/Redeem")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<InviteRedeemResult>> Redeem([FromRoute] string token, [FromBody] RedeemRequest request, CancellationToken cancellationToken)
    {
        PublicPage.ApplyHardeningHeaders(Response);
        if (Throttled(InviteEndpoint.Redeem) is { } throttled)
        {
            return throttled;
        }

        var result = await _invites.RedeemAsync(token, request?.Pin, request?.Username, request?.Password, StolonTranslations.RequestLanguage(Request), cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gives the start of invite links: the address in Settings, or empty for the address the page is open at.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The address with no trailing slash, or empty.</returns>
    public static string LinkBase(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return (config.InviteSettings?.LinkUrl ?? string.Empty).Trim().TrimEnd('/');
    }

    // Counts the call against the caller's address and answers 429 past the limit, in the shape the page reads.
    private ObjectResult? Throttled(InviteEndpoint endpoint)
    {
        var remote = HttpContext?.Connection.RemoteIpAddress;
        WarnIfProxyIsNotTrusted(remote);
        var client = ClientAddress.Key(remote);
        if (_limiter.TryAcquire(endpoint, client))
        {
            return null;
        }

        Response.Headers.RetryAfter = _limiter.RetryAfterSeconds(endpoint, client).ToString(CultureInfo.InvariantCulture);
        var message = StolonTranslations.Get(TranslationKeys.InvitesErrorTooManyAttempts, StolonTranslations.RequestLanguage(Request));
        object body = endpoint == InviteEndpoint.Info
            ? new InviteInfoView(false, string.Empty, false, true, message)
            : InviteRedeemResult.Fail(message);
        return StatusCode(StatusCodes.Status429TooManyRequests, body);
    }

    // Jellyfin reads X-Forwarded-For only from a known proxy, so a private address that still sends it is a proxy it
    // does not trust, and every visitor shares one budget. Logged once for the life of the server.
    private void WarnIfProxyIsNotTrusted(IPAddress? remote)
    {
        if (!ClientAddress.IsPrivate(remote) || Request?.Headers.ContainsKey("X-Forwarded-For") != true || !_limiter.ClaimProxyWarning())
        {
            return;
        }

        _logger.LogWarning(
            "Invite requests arrive from {Address} with an X-Forwarded-For header, which looks like a reverse proxy that is not in Jellyfin's Known proxies. Every invitee then shares that address and one attempt limit. Add the proxy under Dashboard > Networking > Known proxies",
            remote);
    }
}
