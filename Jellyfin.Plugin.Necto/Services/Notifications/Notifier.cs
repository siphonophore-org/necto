using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Sends events to the server log. Notifications replaces the body with its channels.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(INotifier))]
public sealed class Notifier : INotifier
{
    private readonly ILogger<Notifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Notifier"/> class.
    /// </summary>
    /// <param name="logger">Writes each event.</param>
    public Notifier(ILogger<Notifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        _logger.LogDebug("Event {Key}: {Title}", nectoEvent.Key, nectoEvent.Title);
        return Task.CompletedTask;
    }
}
