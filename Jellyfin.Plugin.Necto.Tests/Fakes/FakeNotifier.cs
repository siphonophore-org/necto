using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Notifications;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// A notifier that keeps every event a tool sends, so a test can check which went out.
/// </summary>
public sealed class FakeNotifier : INotifier
{
    /// <summary>Gets every event sent, in order.</summary>
    public List<NectoEvent> Sent { get; } = new();

    /// <summary>Gets every event sent, in order. The same list as <see cref="Sent"/>.</summary>
    public List<NectoEvent> Events => Sent;

    /// <summary>Gets the keys of every event sent, in order.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (Sent)
            {
                return Sent.Select(e => e.Key).ToList();
            }
        }
    }

    /// <inheritdoc />
    public Task SendAsync(NectoEvent nectoEvent, CancellationToken cancellationToken = default)
    {
        lock (Sent)
        {
            Sent.Add(nectoEvent);
        }

        return Task.CompletedTask;
    }
}
