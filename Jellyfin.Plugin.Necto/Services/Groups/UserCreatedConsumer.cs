using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Invites;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Puts a new user who is not an administrator in the group set for new users, moves them onto password rules when
/// those hold for them, and writes their group's template onto them. An account an invite makes is left to the invite,
/// and held disabled until its password is set.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IEventConsumer<UserCreatedEventArgs>))]
public sealed class UserCreatedConsumer : IEventConsumer<UserCreatedEventArgs>
{
    private readonly IConfigurationStore _store;
    private readonly PasswordEnrollment _enrollment;
    private readonly PolicyTemplateService _templates;
    private readonly IUserManager _userManager;
    private readonly ILogger<UserCreatedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserCreatedConsumer"/> class.
    /// </summary>
    /// <param name="store">Reads and changes the groups.</param>
    /// <param name="enrollment">Moves the user onto password rules.</param>
    /// <param name="templates">Writes the group's template onto the user.</param>
    /// <param name="userManager">Holds an invited account disabled.</param>
    /// <param name="logger">Writes the server log.</param>
    public UserCreatedConsumer(IConfigurationStore store, PasswordEnrollment enrollment, PolicyTemplateService templates, IUserManager userManager, ILogger<UserCreatedConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _enrollment = enrollment;
        _templates = templates;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(UserCreatedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var user = eventArgs.Argument;
        if (user.HasPermission(PermissionKind.IsAdministrator))
        {
            return;
        }

        // Core makes the account with no password, and an invite sets it next, so the account waits disabled for it.
        if (InviteRedemptionScope.IsActive)
        {
            await HoldDisabledAsync(user).ConfigureAwait(false);
            return;
        }

        try
        {
            _store.Mutate(c =>
            {
                var group = c.GroupSettings.NewUserGroupId is { } id ? c.Groups.FirstOrDefault(g => g.Id == id) : null;
                if (group is null || group.MemberIds.Contains(user.Id))
                {
                    return false;
                }

                group.MemberIds.Add(user.Id);
                return true;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not add new user {UserId} to the group for new users", user.Id);
        }

        await _enrollment.ReconcileUserAsync(user).ConfigureAwait(false);

        try
        {
            await _templates.ApplyAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the group template onto new user {UserId}", user.Id);
        }
    }

    private async Task HoldDisabledAsync(Jellyfin.Database.Implementations.Entities.User user)
    {
        try
        {
            var policy = _userManager.GetUserDto(user, string.Empty).Policy;
            if (!policy.IsDisabled)
            {
                policy.IsDisabled = true;
                await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hold invited user {UserId} disabled until their password is set", user.Id);
        }
    }
}
