using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Notifications;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Keeps every event a tool sends, in place of Notifications.
/// </summary>
public sealed class FakeNotifier : INotifier
{
    public List<NectoEvent> Events { get; } = new();

    public Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
    {
        lock (Events)
        {
            Events.Add(nectoEvent);
        }

        return Task.CompletedTask;
    }
}
