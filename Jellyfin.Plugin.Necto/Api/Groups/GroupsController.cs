using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Groups;

/// <summary>
/// The routes of the Groups tool: the groups, the users and their groups, a user's permissions, the password rules, and
/// the pending password resets. Every route is for administrators.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Groups")]
[Produces(MediaTypeNames.Application.Json)]
public class GroupsController : ControllerBase
{
    private readonly GroupStore _groups;
    private readonly PermissionService _permissions;
    private readonly PasswordEnrollment _enrollment;
    private readonly ResetCodeService _resetCodes;
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;
    private readonly PolicyTemplateService _templates;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupsController"/> class.
    /// </summary>
    /// <param name="groups">Lists and changes the groups.</param>
    /// <param name="permissions">Works out what a user may do.</param>
    /// <param name="enrollment">Moves users onto or off password rules after a change.</param>
    /// <param name="resetCodes">Reads the pending password resets.</param>
    /// <param name="store">Reads the settings and the groups.</param>
    /// <param name="userManager">Lists the server's users.</param>
    /// <param name="templates">Writes each user's group template after a change.</param>
    public GroupsController(GroupStore groups, PermissionService permissions, PasswordEnrollment enrollment, ResetCodeService resetCodes, IConfigurationStore store, IUserManager userManager, PolicyTemplateService templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _templates = templates;
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(resetCodes);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        _groups = groups;
        _permissions = permissions;
        _enrollment = enrollment;
        _resetCodes = resetCodes;
        _store = store;
        _userManager = userManager;
    }

    /// <summary>
    /// Lists every group, Everyone first.
    /// </summary>
    /// <returns>The groups.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Group>> ListGroups() => Ok(_groups.List());

    /// <summary>
    /// Gives one group.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <returns>The group, or 404 when none has the id.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<Group> GetGroup([FromRoute] Guid id)
        => _groups.Get(id) is { } group ? Ok(group) : NotFound(Error(GroupChangeError.NotFound));

    /// <summary>
    /// Creates a group, then moves its members onto or off password rules and writes their templates.
    /// </summary>
    /// <param name="group">The group's name, members, and permissions. Any id it carries is replaced.</param>
    /// <returns>The group as saved, or 400 with the reason.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Group>> CreateGroup([FromBody] Group group)
    {
        if (group is null)
        {
            return BadRequest(Error(GroupChangeError.NameRequired));
        }

        var change = _groups.Create(group);
        if (!change.Succeeded)
        {
            return BadRequest(Error(change.Error));
        }

        await _enrollment.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
        await _templates.ApplyAllAsync(null, CancellationToken.None, change.Group!.MemberIds).ConfigureAwait(false);
        return Ok(change.Group);
    }

    /// <summary>
    /// Saves a group's name, members, and permissions, then moves users onto or off password rules and writes their templates.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <param name="group">The group's new name, members, and permissions.</param>
    /// <returns>The group as saved, 400 with the reason, or 404 when none has the id.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Group>> UpdateGroup([FromRoute] Guid id, [FromBody] Group group)
    {
        if (group is null)
        {
            return BadRequest(Error(GroupChangeError.NameRequired));
        }

        var before = _groups.Get(id)?.MemberIds ?? new List<Guid>();
        var change = _groups.Update(id, group);
        if (change.Error == GroupChangeError.NotFound)
        {
            return NotFound(Error(change.Error));
        }

        if (!change.Succeeded)
        {
            return BadRequest(Error(change.Error));
        }

        await _enrollment.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
        var joined = change.Group!.MemberIds.Except(before).ToList();
        await _templates.ApplyAllAsync(null, CancellationToken.None, joined).ConfigureAwait(false);
        return Ok(change.Group);
    }

    /// <summary>
    /// Deletes a group, then moves users onto or off password rules and writes their templates. Its members take
    /// Everyone.
    /// </summary>
    /// <param name="id">The group's id.</param>
    /// <returns>204 when deleted, 400 for Everyone, or 404 when none has the id.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteGroup([FromRoute] Guid id)
    {
        var change = _groups.Delete(id);
        if (change.Error == GroupChangeError.NotFound)
        {
            return NotFound(Error(change.Error));
        }

        if (!change.Succeeded)
        {
            return BadRequest(Error(change.Error));
        }

        await _enrollment.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
        await _templates.ApplyAllAsync(null, CancellationToken.None).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Lists the server's users with the groups that apply to each, and the expiry and inactivity those groups give.
    /// </summary>
    /// <returns>The users, by name.</returns>
    [HttpGet("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GroupUser>> ListUsers()
    {
        var users = _userManager.GetUsers().OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToList();
        var result = _store.Read(c => users.Select(u =>
        {
            var admin = u.HasPermission(PermissionKind.IsAdministrator);
            var groups = admin ? Array.Empty<Group>() : GroupMembership.GroupsOf(c, u.Id);
            return new GroupUser
            {
                Id = u.Id,
                Name = u.Username,
                IsAdministrator = admin,
                IsDisabled = u.HasPermission(PermissionKind.IsDisabled),
                GroupIds = admin ? new List<Guid>() : GroupMembership.GroupIdsOf(c, u.Id),
                ExpiresOn = admin ? null : AccountRuleMerger.ExpiresOn(groups),
                InactiveDays = admin ? null : AccountRuleMerger.InactiveDays(groups)
            };
        }).ToList());

        foreach (var row in result)
        {
            if (users.FirstOrDefault(u => u.Id == row.Id) is { } user)
            {
                var dto = _userManager.GetUserDto(user, string.Empty);
                row.PrimaryImageTag = dto?.PrimaryImageTag;
                row.LastActivityDate = dto?.LastActivityDate ?? dto?.LastLoginDate;
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Gives what a user may do in every tool, merged from the groups that apply to them.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The user's permissions, or 404 when the server has no such user.</returns>
    [HttpGet("Users/{userId:guid}/Permissions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<EffectivePermissions> GetUserPermissions([FromRoute] Guid userId)
    {
        var permissions = _permissions.For(userId);
        return permissions.Exists
            ? Ok(permissions)
            : NotFound(new ApiError(StolonTranslations.Get(TranslationKeys.ErrorUserNotFound, StolonTranslations.RequestLanguage(Request))));
    }

    /// <summary>
    /// Drops administrators and deleted users from the groups, and moves every user onto or off password rules.
    /// </summary>
    /// <param name="cancellationToken">Stops the pass between users.</param>
    /// <returns>How many users moved.</returns>
    [HttpPost("PasswordRules/Apply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<PasswordRulesApplied>> ApplyPasswordRules(CancellationToken cancellationToken)
    {
        _groups.CleanMembership();
        var moved = await _enrollment.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new PasswordRulesApplied(moved));
    }

    /// <summary>
    /// Lists the pending password resets with their codes, while Settings shows reset codes.
    /// </summary>
    /// <returns>Whether codes are shown, and the resets when they are.</returns>
    [HttpGet("ResetCodes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ResetCodes> ListResetCodes()
    {
        var enabled = _store.Read(c => c.AccountSettings?.ShowResetCodes == true);
        return Ok(new ResetCodes { Enabled = enabled, Codes = enabled ? _resetCodes.ReadAll() : new List<ResetCode>() });
    }

    private ApiError Error(GroupChangeError error)
    {
        var key = error switch
        {
            GroupChangeError.NotFound => TranslationKeys.ErrorGroupNotFound,
            GroupChangeError.NameTaken => TranslationKeys.ErrorGroupNameTaken,
            GroupChangeError.EveryoneFixed => TranslationKeys.ErrorEveryoneFixed,
            _ => TranslationKeys.ErrorGroupNameRequired
        };
        return new ApiError(StolonTranslations.Get(key, StolonTranslations.RequestLanguage(Request)));
    }
}
