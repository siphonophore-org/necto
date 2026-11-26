using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Services.Accounts;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Puts a new user who is not an administrator in the group set for new users, then moves them onto password rules
/// when those hold for them.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IEventConsumer<UserCreatedEventArgs>))]
public sealed class UserCreatedConsumer : IEventConsumer<UserCreatedEventArgs>
{
    private readonly IConfigurationStore _store;
    private readonly PasswordEnrollment _enrollment;
    private readonly ILogger<UserCreatedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserCreatedConsumer"/> class.
    /// </summary>
    /// <param name="store">Reads and changes the groups.</param>
    /// <param name="enrollment">Moves the user onto password rules.</param>
    /// <param name="logger">Writes the server log.</param>
    public UserCreatedConsumer(IConfigurationStore store, PasswordEnrollment enrollment, ILogger<UserCreatedConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(enrollment);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _enrollment = enrollment;
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
    }
}
