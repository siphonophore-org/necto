using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Stops a library scan before it starts while a disk it would scan is missing, so Jellyfin does not remove that
/// disk's items. Every full scan, scheduled or started by hand, runs as Jellyfin's Scan media library task, and
/// the task manager announces it before its work begins.
/// </summary>
public sealed class ScanGuard : IHostedService
{
    /// <summary>The key of Jellyfin's Scan media library task.</summary>
    public const string LibraryScanKey = "RefreshLibrary";

    /// <summary>How long the guard waits for a fresh check before it decides from the last one.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(45);

    private readonly ITaskManager _tasks;
    private readonly StorageService _storage;
    private readonly StorageSettings _settings;
    private readonly StorageActivity _activity;
    private readonly ILogger<ScanGuard> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanGuard"/> class.
    /// </summary>
    /// <param name="tasks">Announces each task as it starts.</param>
    /// <param name="storage">Checks the disks.</param>
    /// <param name="settings">Gives when scans are blocked.</param>
    /// <param name="activity">Records a blocked scan.</param>
    /// <param name="logger">The logger.</param>
    public ScanGuard(ITaskManager tasks, StorageService storage, StorageSettings settings, StorageActivity activity, ILogger<ScanGuard> logger)
    {
        _tasks = tasks;
        _storage = storage;
        _settings = settings;
        _activity = activity;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tasks.TaskExecuting += OnTaskExecuting;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _tasks.TaskExecuting -= OnTaskExecuting;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks the disks as a scan starts, and cancels the scan when a disk it would scan cannot be read.
    /// </summary>
    /// <param name="worker">The task that is starting.</param>
    /// <returns>The disks that blocked the scan, or none when it may run.</returns>
    public IReadOnlyList<DiskInfo> Guard(IScheduledTaskWorker worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var mode = _settings.Current.ScanGuard;
        if (mode == ScanGuardMode.Off || worker.ScheduledTask.Key != LibraryScanKey)
        {
            return Array.Empty<DiskInfo>();
        }

        var blockers = ScanGuardPolicy.Blockers(mode, CurrentDisks());
        if (blockers.Count == 0)
        {
            return blockers;
        }

        try
        {
            _tasks.Cancel(worker);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "Library scan ended before it could be blocked");
            return Array.Empty<DiskInfo>();
        }

        _logger.LogWarning("Blocked a library scan because {Disks} can't be read", string.Join(", ", blockers.Select(d => d.MountPoint)));
        _activity.ScanBlocked(blockers);
        return blockers;
    }

    private void OnTaskExecuting(object? sender, GenericEventArgs<IScheduledTaskWorker> e) => Guard(e.Argument);

    // The task manager waits for this handler before the scan begins, so the check runs to completion here.
    private IReadOnlyList<DiskInfo> CurrentDisks()
    {
        using var timeout = new CancellationTokenSource(CheckTimeout);
        try
        {
            return _storage.CheckAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Disk check took longer than {Timeout}, so the scan guard used what it last knew", CheckTimeout);
            return _storage.Known;
        }
    }
}
