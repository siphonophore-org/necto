using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Answers what a Jellyfin user may do in every Necto tool, from the groups that apply to them. Administrators may do
/// everything. Every tool calls this before it acts for a user.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class PermissionService
{
    private readonly IConfigurationStore _store;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PermissionService"/> class.
    /// </summary>
    /// <param name="store">Reads the groups.</param>
    /// <param name="userManager">Finds the user and whether they are an administrator.</param>
    public PermissionService(IConfigurationStore store, IUserManager userManager)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(userManager);
        _store = store;
        _userManager = userManager;
    }

    /// <summary>
    /// Works out what a user may do.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The user's permissions, which allow nothing when the server has no such user.</returns>
    public EffectivePermissions For(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        return user is null ? PermissionMerger.Nobody(userId) : For(user);
    }

    /// <summary>
    /// Works out what a user may do.
    /// </summary>
    /// <param name="user">The Jellyfin user.</param>
    /// <returns>The user's permissions.</returns>
    public EffectivePermissions For(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var isAdministrator = user.HasPermission(PermissionKind.IsAdministrator);
        return _store.Read(config => Resolve(config, user.Id, isAdministrator));
    }

    /// <summary>
    /// Works out what a user may do from a configuration.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The user.</param>
    /// <param name="isAdministrator">Whether the user is an administrator.</param>
    /// <returns>The user's permissions.</returns>
    public static EffectivePermissions Resolve(PluginConfiguration config, Guid userId, bool isAdministrator)
    {
        ArgumentNullException.ThrowIfNull(config);
        return isAdministrator
            ? PermissionMerger.Administrator(userId)
            : PermissionMerger.Merge(userId, GroupMembership.GroupsOf(config, userId));
    }
}
