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
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// The routes of a member's own invites, for any signed in user. What a member may do comes from their groups.
/// </summary>
[ApiController]
[Authorize]
[Route("Invites/Mine")]
[Produces(MediaTypeNames.Application.Json)]
public class MemberInvitesController : ControllerBase, IActionFilter
{
    private readonly InviteService _invites;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemberInvitesController"/> class.
    /// </summary>
    /// <param name="invites">Makes and deletes the member's invites.</param>
    /// <param name="store">Reads the settings and the groups.</param>
    /// <param name="userManager">Finds the caller.</param>
    public MemberInvitesController(InviteService invites, IConfigurationStore store, IUserManager userManager)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    /// <summary>Answers not found for every route while Invites is off.</summary>
    /// <param name="context">The action about to run.</param>
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ToolSwitches.IsOn(ToolSwitches.Invites))
        {
            context.Result = NotFound();
        }
    }

    /// <inheritdoc />
    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>
    /// Gives a signed in member their invites, what their groups let them do, and the groups they may invite to.
    /// </summary>
    /// <returns>The member's invites and limits.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<MemberInvitesView> Mine()
    {
        var userId = Caller.UserId(User);
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return Forbid();
        }

        var admin = user.HasPermission(PermissionKind.IsAdministrator);
        var language = StolonTranslations.RequestLanguage(Request);
        var view = _store.Read(c =>
        {
            var p = PermissionService.Resolve(c, userId, admin);
            var groups = new[] { c.Everyone ?? PluginConfiguration.NewEveryone() }
                .Concat(c.Groups)
                .Where(g => p.MayInviteTo(g.Id) && !InviteNormalizer.BlocksInvites(g))
                .Select(g => new InviteGroupView(g.Id, g.IsEveryone ? StolonTranslations.Get(TranslationKeys.GroupEveryone, language) : g.Name))
                .ToList();
            return new MemberInvitesView(p.Invites.MayCreate, p.Invites.MaxInvites, groups, InvitesController.LinkBase(c), new List<InviteSummary>());
        });
        return Ok(view with { Invites = _invites.SummariesOf(userId) });
    }

    /// <summary>
    /// Makes an invite for the signed in member, within what their groups allow.
    /// </summary>
    /// <param name="request">What it holds.</param>
    /// <returns>The invite, or 400 with the reason.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<InviteSummary> CreateMine([FromBody] InviteRequest request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var (invite, error) = _invites.CreateForMember(Caller.UserId(User), request, StolonTranslations.RequestLanguage(Request));
        return error is null ? Ok(invite) : BadRequest(new ApiError(error));
    }

    /// <summary>
    /// Deletes one of the signed in member's invites.
    /// </summary>
    /// <param name="id">The invite.</param>
    /// <returns>204, or 404 when the member has no such invite.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteMine([FromRoute] Guid id)
    {
        var userId = Caller.UserId(User);
        return userId != Guid.Empty && _invites.Delete(id, userId) ? NoContent() : NotFound();
    }
}
