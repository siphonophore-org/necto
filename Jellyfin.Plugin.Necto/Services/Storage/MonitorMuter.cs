using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Makes Jellyfin's real-time monitoring ignore the folders of a missing disk until it is back, so a disk that
/// drops out does not set off removals through the file watcher.
/// </summary>
public sealed class MonitorMuter
{
    private readonly ILibraryMonitor _monitor;
    private readonly ILogger<MonitorMuter> _logger;
    private readonly HashSet<string> _muted = new(StringComparer.FromComparison(MountPaths.Comparison));
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorMuter"/> class.
    /// </summary>
    /// <param name="monitor">Jellyfin's real-time monitor.</param>
    /// <param name="logger">The logger.</param>
    public MonitorMuter(ILibraryMonitor monitor, ILogger<MonitorMuter> logger)
    {
        _monitor = monitor;
        _logger = logger;
    }

    /// <summary>
    /// Mutes the folders of every disk that cannot be read, and releases the folders of every disk that is back.
    /// </summary>
    /// <param name="disks">The disks a check saw.</param>
    /// <param name="enabled"><c>false</c> releases every folder, as when muting is turned off.</param>
    public void Update(IEnumerable<DiskInfo> disks, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(disks);
        var wanted = enabled
            ? disks.Where(d => d.State != DiskState.Available).SelectMany(d => d.Folders.Select(f => f.Path)).ToHashSet(StringComparer.FromComparison(MountPaths.Comparison))
            : new HashSet<string>();

        lock (_lock)
        {
            var paused = 0;
            var resumed = 0;
            foreach (var folder in wanted.Where(f => !_muted.Contains(f)))
            {
                paused++;
                _monitor.ReportFileSystemChangeBeginning(folder);
                _muted.Add(folder);
                _logger.LogDebug("Muted real-time monitoring for {Path}", folder);
            }

            foreach (var folder in _muted.Where(f => !wanted.Contains(f)).ToList())
            {
                // Jellyfin keeps ignoring a released folder for 45 seconds, so a disk settling back in sets off nothing.
                _monitor.ReportFileSystemChangeComplete(folder, false);
                resumed++;
                _muted.Remove(folder);
                _logger.LogDebug("Released real-time monitoring for {Path}", folder);
            }

            // One line per change, since pausing the monitor changes how Jellyfin itself behaves.
            if (paused > 0)
            {
                _logger.LogInformation("Paused real-time monitoring for {Count} folders on missing disks", paused);
            }

            if (resumed > 0)
            {
                _logger.LogInformation("Resumed real-time monitoring for {Count} folders", resumed);
            }
        }
    }

    /// <summary>
    /// Tells whether a folder is muted now.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns><c>true</c> while it is muted.</returns>
    public bool IsMuted(string folder)
    {
        lock (_lock)
        {
            return _muted.Contains(folder);
        }
    }
}
