using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Keeps the latest sends in memory, newest first, for the send log in Troubleshooting. It holds
/// <see cref="Capacity"/> entries and starts empty when the server starts.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class NotificationLog
{
    /// <summary>The most entries kept.</summary>
    public const int Capacity = 200;

    private readonly LinkedList<NotificationLogEntry> _entries = new();
    private readonly object _lock = new();

    /// <summary>
    /// Adds an entry, dropping the oldest past <see cref="Capacity"/>.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Add(NotificationLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    /// <summary>
    /// Lists the entries, newest first.
    /// </summary>
    /// <returns>A copy of the entries.</returns>
    public IReadOnlyList<NotificationLogEntry> List()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    /// <summary>
    /// Drops every entry.
    /// </summary>
    /// <returns>How many were dropped.</returns>
    public int Clear()
    {
        lock (_lock)
        {
            var count = _entries.Count;
            _entries.Clear();
            return count;
        }
    }
}
