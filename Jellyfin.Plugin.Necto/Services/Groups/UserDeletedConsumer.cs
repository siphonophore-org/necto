using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Necto.Services.Configuration;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Drops a deleted user from every group and from the password rule records at once.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IEventConsumer<UserDeletedEventArgs>))]
public sealed class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    private readonly IConfigurationStore _store;
    private readonly ILogger<UserDeletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/> class.
    /// </summary>
    /// <param name="store">Changes the groups.</param>
    /// <param name="logger">Writes the server log.</param>
    public UserDeletedConsumer(IConfigurationStore store, ILogger<UserDeletedConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var userId = eventArgs.Argument.Id;
        try
        {
            _store.Mutate(c =>
            {
                var removed = 0;
                foreach (var group in c.Groups)
                {
                    removed += group.MemberIds.RemoveAll(id => id == userId);
                }

                removed += c.ProviderEnrollments.RemoveAll(e => e.UserId == userId);
                return removed > 0;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not drop deleted user {UserId} from the groups", userId);
        }

        return Task.CompletedTask;
    }
}
