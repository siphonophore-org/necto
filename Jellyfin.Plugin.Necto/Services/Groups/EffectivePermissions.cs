using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// What one user may do in every tool, merged from the groups that apply to them. Every tool reads its section from
/// here through <see cref="PermissionService"/>.
/// </summary>
public sealed class EffectivePermissions
{
    /// <summary>Gets or sets the user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets a value indicating whether the user is an administrator, who may do everything.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>Gets or sets a value indicating whether the user exists on the server.</summary>
    public bool Exists { get; set; } = true;

    /// <summary>Gets or sets the groups the permissions come from, Everyone when the user is in no group.</summary>
    public List<Guid> GroupIds { get; set; } = new();

    /// <summary>Gets or sets the account rules that hold for the user. Administrators are held to none.</summary>
    public EffectiveAccounts Accounts { get; set; } = new();

    /// <summary>Gets or sets what the user may do in Requests.</summary>
    public RequestPermissions Requests { get; set; } = new();

    /// <summary>Gets or sets what the user may do in Issues.</summary>
    public IssuePermissions Issues { get; set; } = new();

    /// <summary>Gets or sets what the user may do in Invites.</summary>
    public InvitePermissions Invites { get; set; } = new();

    /// <summary>Gets or sets what the user may have in WireGuard.</summary>
    public WireGuardPermissions WireGuard { get; set; } = new();

    /// <summary>Gets or sets the scripts that run for the user.</summary>
    public ScriptPermissions Scripts { get; set; } = new();

    /// <summary>Gets or sets the custom pages the user may open.</summary>
    public CustomPagePermissions CustomPages { get; set; } = new();

    /// <summary>Gets or sets the events the user receives.</summary>
    public NotificationPermissions Notifications { get; set; } = new();

    /// <summary>
    /// Works out whether the user may open a custom page.
    /// </summary>
    /// <param name="pageId">The page.</param>
    /// <returns><c>true</c> when an administrator, when any group gives every page, or when any group names it.</returns>
    public bool MayOpenPage(string pageId) => IsAdministrator || CustomPages.AllPages || CustomPages.PageIds.Contains(pageId);

    /// <summary>
    /// Works out whether a script runs for the user.
    /// </summary>
    /// <param name="scriptId">The script.</param>
    /// <returns><c>true</c> when an administrator, when any group runs every script, or when any group names it.</returns>
    public bool RunsScript(string scriptId) => IsAdministrator || Scripts.AllScripts || Scripts.ScriptIds.Contains(scriptId);

    /// <summary>
    /// Works out whether the user receives an event.
    /// </summary>
    /// <param name="eventKey">The event.</param>
    /// <returns><c>true</c> when an administrator, when any group gives every event, or when any group names it.</returns>
    public bool Receives(string eventKey) => IsAdministrator || Notifications.AllEvents || Notifications.EventKeys.Contains(eventKey);

    /// <summary>
    /// Works out whether the user's invites may target a group.
    /// </summary>
    /// <param name="groupId">The group.</param>
    /// <returns><c>true</c> when the user may create invites and the group is one they may target.</returns>
    public bool MayInviteTo(Guid groupId) => IsAdministrator || (Invites.MayCreate && (Invites.AnyGroup || Invites.TargetGroupIds.Contains(groupId)));
}
