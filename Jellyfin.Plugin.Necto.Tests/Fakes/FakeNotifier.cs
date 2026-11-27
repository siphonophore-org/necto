using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Notifications;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Stands in for Notifications, keeping every event a tool sends, in order. It answers at once, so an event sent in
/// the background is kept before the call that sent it returns.
/// </summary>
public sealed class FakeNotifier : INotifier
{
    /// <summary>Gets every event sent.</summary>
    public ConcurrentQueue<NectoEvent> Sent { get; } = new();

    /// <summary>Gets the keys of the events sent, in order.</summary>
    public IReadOnlyList<string> Keys => Sent.Select(e => e.Key).ToList();

    public Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(nectoEvent);
        return Task.CompletedTask;
    }
}
