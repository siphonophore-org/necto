using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Keeps Jellyfin messages for a user with no app online until one comes online, in a file beside the configuration.
/// A message held longer than <see cref="MaxAge"/> is dropped.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(INotifyHeldStore))]
public sealed class NotificationHeldStore : INotifyHeldStore
{
    /// <summary>The file the messages are kept in, in the plugin configurations folder.</summary>
    public const string FileName = "Jellyfin.Plugin.Necto.Held.json";

    /// <summary>The most messages kept for one user, so someone who never signs in does not grow the file without end.</summary>
    public const int MaxPerUser = 50;

    /// <summary>Gets how long a message is kept for a user who does not come online.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromDays(30);

    private readonly JsonFileStore<List<NotifyHeldMessage>> _file;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationHeldStore"/> class.
    /// </summary>
    /// <param name="paths">Gives the folder the messages are kept in.</param>
    /// <param name="logger">Writes the server log.</param>
    public NotificationHeldStore(IApplicationPaths paths, ILogger<NotificationHeldStore> logger)
        : this(Path.Join((paths ?? throw new ArgumentNullException(nameof(paths))).PluginConfigurationsPath, FileName), logger, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationHeldStore"/> class at a path, with a clock.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="time">The clock the age of a message is read against.</param>
    public NotificationHeldStore(string path, ILogger? logger, TimeProvider time)
    {
        _file = new JsonFileStore<List<NotifyHeldMessage>>(path, logger);
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public void Hold(NotifyHeldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var cutoff = _time.GetUtcNow().UtcDateTime - MaxAge;
        _file.Update(list =>
        {
            list.RemoveAll(m => m.CreatedUtc < cutoff || m.Id == message.Id);
            list.Add(message);
            var mine = list.Where(m => m.UserId == message.UserId).OrderBy(m => m.CreatedUtc).ToList();
            foreach (var old in mine.Take(Math.Max(0, mine.Count - MaxPerUser)))
            {
                list.Remove(old);
            }
        });
    }

    /// <inheritdoc />
    public IReadOnlyList<NotifyHeldMessage> Take(Guid userId)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - MaxAge;
        var taken = new List<NotifyHeldMessage>();
        _file.UpdateIf(list =>
        {
            taken = list.Where(m => m.UserId == userId).OrderBy(m => m.CreatedUtc).ToList();
            var removed = list.RemoveAll(m => m.UserId == userId || m.CreatedUtc < cutoff);
            return removed > 0;
        });
        return taken.Where(m => m.CreatedUtc >= cutoff).ToList();
    }

    /// <summary>
    /// Counts the messages held for each user.
    /// </summary>
    /// <returns>The count by user.</returns>
    public IReadOnlyDictionary<Guid, int> Counts() => _file.Load().GroupBy(m => m.UserId).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Drops every held message.
    /// </summary>
    /// <returns>How many were dropped.</returns>
    public int Clear()
    {
        var removed = 0;
        _file.UpdateIf(list =>
        {
            removed = list.Count;
            list.Clear();
            return removed > 0;
        });
        return removed;
    }

    /// <summary>
    /// Drops the messages held about one subject, such as a request that was removed.
    /// </summary>
    /// <param name="subject">The subject the messages were sent with.</param>
    /// <returns>How many were dropped.</returns>
    public int RemoveSubject(string subject)
    {
        var removed = 0;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return removed;
        }

        _file.UpdateIf(list =>
        {
            removed = list.RemoveAll(m => string.Equals(m.Subject, subject, StringComparison.Ordinal));
            return removed > 0;
        });
        return removed;
    }

    /// <summary>
    /// Drops the messages held for a user, such as one who was deleted.
    /// </summary>
    /// <param name="userId">The user.</param>
    public void RemoveUser(Guid userId) => _file.UpdateIf(list => list.RemoveAll(m => m.UserId == userId) > 0);
}
