using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Takes an event from any tool and sends it to the channels and people the groups choose. A tool never sends a
/// notification itself.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends one event. It never throws for a channel that fails.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="cancellationToken">Stops the send.</param>
    /// <returns>A task that completes when every channel has been tried.</returns>
    Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default);
}
