using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Siphonophore.Stolon;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Checks every disk and share the libraries use: where each library folder sits, whether its disk is mounted,
/// and how full it is. Each check records what changed in the activity log and mutes real-time monitoring for
/// disks that cannot be read.
/// </summary>
public sealed class StorageService : IDisposable
{
    /// <summary>How long one disk call may take before its disk counts as unresponsive.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How long one remount may take, including the wait for the disk to show up.</summary>
    public static readonly TimeSpan RemountTimeout = TimeSpan.FromSeconds(25);

    /// <summary>How long a remount that worked waits for the disk to show up at its mount point.</summary>
    public static readonly TimeSpan MountWait = TimeSpan.FromSeconds(10);

    // How often the wait for a remounted disk looks for it.
    private static readonly TimeSpan MountPoll = TimeSpan.FromMilliseconds(500);

    // Tries a missing disk at every check three times, then waits an hour, doubling up to half a day.
    private static readonly BackoffPolicy RemountBackoff = new(3, TimeSpan.FromHours(1), TimeSpan.FromHours(12));

    private readonly ILibraryManager _libraryManager;
    private readonly IMountTable _mounts;
    private readonly IDiskInventory _inventory;
    private readonly ISmartReader _smart;
    private readonly IRemounter _remounter;
    private readonly JellyfinFolders? _jellyfin;
    private readonly VolumeProbe _probe;
    private readonly StorageSettings _settings;
    private readonly StorageActivity _activity;
    private readonly MonitorMuter _muter;
    private readonly TimeProvider _time;
    private readonly ILogger<StorageService> _logger;
    private readonly JsonFileStore<StorageState> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<DiskInfo> _last = Array.Empty<DiskInfo>();

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageService"/> class.
    /// </summary>
    /// <param name="libraryManager">Lists the libraries and their folders.</param>
    /// <param name="mounts">Reads the OS's mounted volumes.</param>
    /// <param name="inventory">Finds whole disks with no file system.</param>
    /// <param name="smart">Reads each disk's SMART data.</param>
    /// <param name="remounter">Mounts a missing disk again.</param>
    /// <param name="probe">Runs each disk call with a time limit.</param>
    /// <param name="settings">Gives the stops and whether to mute monitoring.</param>
    /// <param name="activity">Writes changes to the activity log.</param>
    /// <param name="muter">Mutes real-time monitoring for disks that cannot be read.</param>
    /// <param name="applicationPaths">Locates the state file.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="jellyfin">Jellyfin's own folders, which put their disks in use, or <c>null</c> for none.</param>
    public StorageService(
        ILibraryManager libraryManager,
        IMountTable mounts,
        IDiskInventory inventory,
        ISmartReader smart,
        IRemounter remounter,
        VolumeProbe probe,
        StorageSettings settings,
        StorageActivity activity,
        MonitorMuter muter,
        IApplicationPaths applicationPaths,
        TimeProvider time,
        ILogger<StorageService> logger,
        JellyfinFolders? jellyfin = null)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _jellyfin = jellyfin;
        _libraryManager = libraryManager;
        _mounts = mounts;
        _inventory = inventory;
        _smart = smart;
        _remounter = remounter;
        _probe = probe;
        _settings = settings;
        _activity = activity;
        _muter = muter;
        _time = time;
        _logger = logger;
        _store = new JsonFileStore<StorageState>(Path.Combine(applicationPaths.PluginConfigurationsPath, "Necto.Storage.State.json"), logger);
    }

    /// <summary>
    /// Gets what the last finished check saw, or nothing before the first check.
    /// </summary>
    public IReadOnlyList<DiskInfo> Last => Volatile.Read(ref _last);

    /// <summary>
    /// Gets what is known of the disks without checking them: the last finished check, or before the first one, the
    /// disks the state file recorded as missing, as missing disks that are not safe to scan. The scan guard uses it
    /// when a check can't finish in time, so it never lets a scan through for want of an answer.
    /// </summary>
    public IReadOnlyList<DiskInfo> Known
    {
        get
        {
            var last = Last;
            if (last.Count > 0)
            {
                return last;
            }

            return _store.Load().MissingSince.Select(m => new DiskInfo
            {
                MountPoint = m.Key,
                Name = MountPaths.DisplayName(m.Key, null),
                State = DiskState.Missing,
                InUse = true,
                ScanSafe = false,
                MissingSinceUtc = m.Value
            }).ToList();
        }
    }

    /// <summary>
    /// Checks every disk the libraries use. One check runs at a time.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per disk, missing and unresponsive disks first.</returns>
    public async Task<IReadOnlyList<DiskInfo>> CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var disks = await CheckOnceAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _last, disks);
            return disks;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<IReadOnlyList<DiskInfo>> CheckOnceAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var comparer = StringComparer.FromComparison(MountPaths.Comparison);
        var state = WithComparer(_store.Load(), comparer);
        var now = _time.GetUtcNow().UtcDateTime;
        var disks = await ReadDisksAsync(state, cancellationToken).ConfigureAwait(false);

        // A disk mounted again is read again, so this check sees it as it is now.
        var (remounted, notRemounted) = await RemountAsync(disks, state, settings, now, cancellationToken).ConfigureAwait(false);
        if (remounted.Count > 0)
        {
            disks = await ReadDisksAsync(state, cancellationToken).ConfigureAwait(false);
            var back = new HashSet<string>(remounted, comparer);
            remounted = disks.Where(d => back.Contains(d.MountPoint) && d.State == DiskState.Available).Select(d => d.MountPoint).ToList();
        }

        var memory = new Dictionary<string, DiskMemory>(state.Disks, comparer);
        foreach (var disk in disks.Where(d => d.State == DiskState.Available && d.TotalBytes is > 0))
        {
            var before = memory.TryGetValue(disk.MountPoint, out var seen) ? seen.Level : SpaceLevel.None;
            var (warning, error, own) = AlertOrder.For(settings, disk.MountPoint);
            disk.Level = SpaceLevels.Evaluate(disk.TotalBytes!.Value, disk.FreeBytes ?? 0, warning, error, before);
            disk.WarningPercent = SpaceLevels.Threshold(warning, disk.TotalBytes.Value);
            disk.ErrorPercent = SpaceLevels.Threshold(error, disk.TotalBytes.Value);
            disk.WarningStop = warning.Enabled ? warning : null;
            disk.ErrorStop = error.Enabled ? error : null;
            disk.OwnAlerts = own;
        }

        // A disk's SMART readings against the health alerts, a hot disk clearing only once it has cooled a margin.
        var health = settings.HealthAlerts ?? new HealthAlerts();
        foreach (var disk in disks.Where(d => d.State == DiskState.Available))
        {
            var wasHot = memory.TryGetValue(disk.MountPoint, out var seen) && seen.Hot;
            disk.Hot = HealthLevels.IsHot(disk.TemperatureCelsius, health, wasHot);
            disk.Worn = HealthLevels.IsWorn(disk.WearPercent, health);
        }

        // Only disks a library uses are recorded and guarded. A disk no library uses can come and go freely.
        var inUse = disks.Where(d => d.InUse).ToList();

        // A disk that can't be read keeps the kind it was last seen as, so a missing USB drive still reads External.
        foreach (var disk in inUse.Where(d => d.State != DiskState.Available && d.Kind == DiskKind.Local))
        {
            if (memory.TryGetValue(disk.MountPoint, out var seen) && seen.Kind != DiskKind.Local)
            {
                disk.Kind = seen.Kind;
            }
        }
        foreach (var disk in inUse)
        {
            disk.TakesAddress = _remounter.TakesAddress;
            disk.ShareAddress = AddressFor(settings, state, disk.MountPoint);
        }

        var remountedSet = new HashSet<string>(remounted, comparer);

        // A disk mounted again is recorded as remounted, which says it is back.
        var events = DiskEvents.Compare(inUse, memory, state.MissingSince, now, health)
            .Where(e => !(e.Kind == DiskEventKind.Returned && remountedSet.Contains(e.Disk.MountPoint)))
            .ToList();

        var since = new Dictionary<string, DateTime>(comparer);
        foreach (var disk in inUse.Where(d => d.State != DiskState.Available))
        {
            disk.MissingSinceUtc = state.MissingSince.TryGetValue(disk.MountPoint, out var first) ? first : now;
            since[disk.MountPoint] = disk.MissingSinceUtc.Value;
        }

        state.MissingSince = since;
        state.Disks = memory;
        _store.Save(state);

        LogChanges(events);
        _activity.Record(events);
        _activity.Remounted(inUse.Where(d => remountedSet.Contains(d.MountPoint)).ToList());
        _activity.NotRemounted(notRemounted);
        _muter.Update(inUse, settings.MuteMonitoring);

        return disks
            .OrderByDescending(d => d.InUse)
            .ThenByDescending(d => d.State)
            .ThenBy(d => d.MountPoint, comparer)
            .ToList();
    }

    // Reads every library folder's disk, the mounted volumes no library uses, and the disks with no file system.
    private async Task<DiskInfo[]> ReadDisksAsync(StorageState state, CancellationToken cancellationToken)
    {
        var folders = ReadFolders();
        var listed = await _probe.RunAsync("mounts", _mounts.GetMountPoints, ProbeTimeout, cancellationToken).ConfigureAwait(false);
        var mountPoints = listed.Value ?? Array.Empty<string>();

        var comparer = StringComparer.FromComparison(MountPaths.Comparison);
        var paths = folders.Select(f => MountPaths.Normalize(f.Path)).Distinct(comparer);
        var checks = await Task.WhenAll(paths.Select(async path =>
        {
            var result = await _probe.RunAsync("folder:" + path, () => _mounts.GetFolder(path), ProbeTimeout, cancellationToken).ConfigureAwait(false);
            return (Path: path, Status: result.Completed ? result.Value : (FolderStatus?)null);
        })).ConfigureAwait(false);
        var statuses = checks.ToDictionary(c => c.Path, c => c.Status, comparer);

        var groups = DiskGrouper.Group(folders, mountPoints, p => statuses.TryGetValue(p, out var found) ? found : FolderStatus.Missing, state, MountPaths.Comparison);
        var used = await Task.WhenAll(groups.Select(g => DescribeAsync(g, cancellationToken))).ConfigureAwait(false);
        var taken = new HashSet<string>(groups.Select(g => g.MountPoint), comparer);
        var spare = await Task.WhenAll(mountPoints.Where(m => !taken.Contains(m)).Distinct(comparer).Select(m => DescribeUnusedAsync(m, cancellationToken))).ConfigureAwait(false);
        var raws = await _probe.RunAsync("inventory", _inventory.GetUnmounted, ProbeTimeout * 3, cancellationToken).ConfigureAwait(false);
        var unformatted = (raws.Value ?? Array.Empty<RawDisk>()).Select(raw => new DiskInfo
        {
            MountPoint = string.Empty,
            Device = raw.Device,
            Model = raw.Model,
            Name = raw.Model ?? raw.Device,
            State = raw.Blank ? DiskState.Unformatted : DiskState.NotMounted,
            Contents = raw.Contents?.ToList() ?? new List<string>(),
            MountsAt = raw.MountsAt,
            InUse = false,
            Kind = raw.External ? DiskKind.External : DiskKind.Local,
            TotalBytes = raw.SizeBytes > 0 ? raw.SizeBytes : null
        });
        return used.Concat(spare.OfType<DiskInfo>()).Concat(unformatted).ToArray();
    }

    // Remembers what is mounted at each disk in use, and mounts each missing one again unless it is waiting out a
    // pause after failed tries. Returns the mount points mounted again, and the disks whose first try failed.
    private async Task<(List<string> Remounted, List<(DiskInfo Disk, RemountResult Result)> Failed)> RemountAsync(
        IReadOnlyList<DiskInfo> disks, StorageState state, StorageConfiguration settings, DateTime now, CancellationToken cancellationToken)
    {
        var comparer = StringComparer.FromComparison(MountPaths.Comparison);
        var known = new Dictionary<string, RemountMemory>(state.Remounts ?? new(), comparer);
        var next = new Dictionary<string, RemountMemory>(comparer);
        var remounted = new List<string>();
        var failed = new List<(DiskInfo, RemountResult)>();
        var tries = new List<(DiskInfo Disk, RemountMemory Memory, string? Source, BackoffState Backoff)>();

        foreach (var disk in disks.Where(d => d.InUse && d.State == DiskState.Available))
        {
            var source = await _probe.RunAsync("source:" + disk.MountPoint, () => _remounter.SourceOf(disk.MountPoint), ProbeTimeout, cancellationToken).ConfigureAwait(false);
            var kept = known.TryGetValue(disk.MountPoint, out var old) ? old.Source : null;
            next[disk.MountPoint] = new RemountMemory { Source = source.Completed ? source.Value : kept };
        }

        foreach (var disk in disks.Where(d => d.InUse && d.State != DiskState.Available))
        {
            var memory = known.TryGetValue(disk.MountPoint, out var found) ? found : new RemountMemory();
            next[disk.MountPoint] = memory;

            // An address set by hand wins over the one seen while it was mounted, and a new one is tried at once.
            var source = Configured(settings, disk.MountPoint) ?? memory.Source;
            var backoff = !string.Equals(source, memory.TriedSource, StringComparison.Ordinal)
                ? BackoffState.Initial
                : new BackoffState(memory.Failures, memory.RetryAfterUtc);

            // An unresponsive disk is still mounted, and mounting it again would not help.
            if (settings.Remount && disk.State == DiskState.Missing && !RemountBackoff.IsBackingOff(backoff, now))
            {
                tries.Add((disk, memory, source, backoff));
            }
        }

        // Every missing disk is tried at once, so several dropped shares take no longer than one.
        var attempts = await Task.WhenAll(tries.Select(async t =>
        {
            var mountPoint = t.Disk.MountPoint;
            var attempt = await _probe.RunAsync("remount:" + mountPoint, () => RemountAndWait(mountPoint, t.Source), RemountTimeout, cancellationToken).ConfigureAwait(false);
            return (t.Disk, t.Memory, t.Source, t.Backoff, Result: attempt.Completed ? attempt.Value! : RemountResult.Failed(RemountFailure.Unreachable));
        })).ConfigureAwait(false);

        foreach (var (disk, memory, source, backoff, result) in attempts)
        {
            var mountPoint = disk.MountPoint;
            if (!result.Tried)
            {
                continue;
            }

            if (result.Mounted)
            {
                _logger.LogInformation("Remounted the disk at {MountPoint}", mountPoint);
                remounted.Add(mountPoint);
                next[mountPoint] = new RemountMemory { Source = memory.Source };
                continue;
            }

            var after = RemountBackoff.RecordFailure(backoff, now);
            next[mountPoint] = new RemountMemory { Source = memory.Source, TriedSource = source, Failures = after.ConsecutiveFailures, RetryAfterUtc = after.BackoffUntilUtc };
            if (backoff.ConsecutiveFailures == 0)
            {
                _logger.LogWarning("Could not remount the disk at {MountPoint}: {Failure} ({Code})", mountPoint, result.Failure, result.Code);
                failed.Add((disk, result));
            }
            else
            {
                _logger.LogDebug("Could not remount the disk at {MountPoint} on try {Try}: {Failure} ({Code})", mountPoint, after.ConsecutiveFailures, result.Failure, result.Code);
            }
        }

        state.Remounts = next;
        return (remounted, failed);
    }

    // The state file's dictionaries compare as JSON leaves them, by exact text, so they are given the OS's comparison,
    // under which D:\ and d:\ are one disk on Windows.
    private static StorageState WithComparer(StorageState state, StringComparer comparer)
    {
        state.FolderMounts = new Dictionary<string, string>(state.FolderMounts ?? new(), comparer);
        state.MissingSince = new Dictionary<string, DateTime>(state.MissingSince ?? new(), comparer);
        state.Disks = new Dictionary<string, DiskMemory>(state.Disks ?? new(), comparer);
        state.Remounts = new Dictionary<string, RemountMemory>(state.Remounts ?? new(), comparer);
        return state;
    }

    // The address set by hand for a mount point, if any.
    private static string? Configured(StorageConfiguration settings, string mountPoint)
        => settings.ShareAddresses?.LastOrDefault(a => a is not null && !string.IsNullOrWhiteSpace(a.Address) && string.Equals(MountPaths.Normalize(a.MountPoint), mountPoint, MountPaths.Comparison))?.Address.Trim();

    // Where a disk is mounted from: the address set by hand, else the one seen while it was mounted.
    private static string? AddressFor(StorageConfiguration settings, StorageState state, string mountPoint)
        => Configured(settings, mountPoint) ?? (state.Remounts.TryGetValue(mountPoint, out var memory) ? memory.Source : null);

    // Mounts a disk again, then waits for it to show up, since some OSes mount it after the call returns.
    private RemountResult RemountAndWait(string mountPoint, string? source)
    {
        var result = _remounter.Remount(mountPoint, source) ?? RemountResult.Unsupported;
        if (!result.Mounted)
        {
            return result;
        }

        for (var waited = TimeSpan.Zero; ; waited += MountPoll)
        {
            // Windows lists no \\server\share path as a drive, so a reachable share counts as mounted.
            var unc = mountPoint.StartsWith(@"\\", StringComparison.Ordinal);
            if (unc ? Directory.Exists(mountPoint) : _mounts.GetMountPoints().Any(m => string.Equals(m, mountPoint, MountPaths.Comparison)))
            {
                return result;
            }

            if (waited >= MountWait)
            {
                return RemountResult.Failed(RemountFailure.NotMounted);
            }

            Thread.Sleep(MountPoll);
        }
    }

    private void LogChanges(IReadOnlyList<DiskEvent> events)
    {
        foreach (var change in events)
        {
            switch (change.Kind)
            {
                case DiskEventKind.WentMissing:
                    _logger.LogWarning("Disk at {MountPoint} is not mounted", change.Disk.MountPoint);
                    break;
                case DiskEventKind.BecameUnresponsive:
                    _logger.LogWarning("Disk at {MountPoint} is unresponsive", change.Disk.MountPoint);
                    break;
                case DiskEventKind.Hot:
                    _logger.LogWarning("Disk at {MountPoint} is at {Celsius} °C", change.Disk.MountPoint, change.Disk.TemperatureCelsius);
                    break;
                case DiskEventKind.Worn:
                    _logger.LogWarning("Disk at {MountPoint} has used {Wear}% of its rated life", change.Disk.MountPoint, change.Disk.WearPercent);
                    break;
                case DiskEventKind.Failing:
                    _logger.LogWarning("Disk at {MountPoint} reports a SMART failure", change.Disk.MountPoint);
                    break;
                case DiskEventKind.Returned:
                    _logger.LogInformation("Disk at {MountPoint} is back", change.Disk.MountPoint);
                    break;
                default:
                    _logger.LogWarning("Disk at {MountPoint} passed its {Stop} stop with {Free} free", change.Disk.MountPoint, change.Kind == DiskEventKind.Full ? "Error" : "Warning", FormatUtilities.FormatBytes(change.Disk.FreeBytes ?? 0));
                    break;
            }
        }
    }

    private List<LibraryFolder> ReadFolders()
    {
        var folders = new List<LibraryFolder>();
        foreach (var library in _libraryManager.GetVirtualFolders())
        {
            // Collections are a folder Jellyfin keeps for itself, not media.
            if (library.CollectionType == CollectionTypeOptions.boxsets)
            {
                continue;
            }

            foreach (var location in library.Locations ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(location))
                {
                    folders.Add(new LibraryFolder(library.ItemId, library.Name, location));
                }
            }
        }

        // Jellyfin's own folders, such as its cache, put their disk in use as well.
        foreach (var (key, path) in _jellyfin?.Get() ?? Array.Empty<(string, string)>())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                folders.Add(new LibraryFolder("jellyfin", key, path, true));
            }
        }

        return folders;
    }

    // A mounted volume no library uses, listed so it can be seen and picked later. Volumes the OS keeps for itself are left out.
    private async Task<DiskInfo?> DescribeUnusedAsync(string mountPoint, CancellationToken cancellationToken)
    {
        var read = await _probe.RunAsync("read:" + mountPoint, () => _mounts.Read(mountPoint), ProbeTimeout, cancellationToken).ConfigureAwait(false);
        if (!read.Completed || read.Value is not { } reading || !MountPaths.IsUserVolume(mountPoint, reading.FileSystem, reading.TotalBytes))
        {
            return null;
        }

        var disk = new DiskInfo
        {
            MountPoint = mountPoint,
            Name = MountPaths.DisplayName(mountPoint, reading.Label),
            InUse = false,
            IsSystem = string.Equals(mountPoint, MountPaths.SystemMountPoint, MountPaths.Comparison),
            Kind = reading.IsNetwork || MountPaths.IsShare(mountPoint) ? DiskKind.Share : DiskKind.Local,
            TotalBytes = reading.TotalBytes,
            FreeBytes = reading.FreeBytes,
            FileSystem = reading.FileSystem
        };
        await MarkExternalAsync(disk, cancellationToken).ConfigureAwait(false);
        await ReadSmartAsync(disk, cancellationToken).ConfigureAwait(false);
        await CheckWritableAsync(disk, cancellationToken).ConfigureAwait(false);
        return disk;
    }

    private async Task<DiskInfo> DescribeAsync(DiskGroup group, CancellationToken cancellationToken)
    {
        var disk = new DiskInfo
        {
            MountPoint = group.MountPoint,
            State = group.State,
            ScanSafe = !group.Unguarded,
            IsSystem = string.Equals(group.MountPoint, MountPaths.SystemMountPoint, MountPaths.Comparison),
            Kind = MountPaths.IsShare(group.MountPoint) ? DiskKind.Share : DiskKind.Local,
            Libraries = group.Libraries.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Folders = group.Folders,
            MissingFolders = group.MissingFolders,
            Name = MountPaths.DisplayName(group.MountPoint, null)
        };

        if (group.State != DiskState.Available)
        {
            return disk;
        }

        var read = await _probe.RunAsync("read:" + group.MountPoint, () => _mounts.Read(group.MountPoint), ProbeTimeout, cancellationToken).ConfigureAwait(false);
        if (!read.Completed)
        {
            _logger.LogDebug("Volume at {MountPoint} did not answer within {Timeout}", group.MountPoint, ProbeTimeout);
            disk.State = DiskState.Unresponsive;
            disk.ScanSafe = false;
            return disk;
        }

        if (read.Value is { } reading)
        {
            disk.TotalBytes = reading.TotalBytes;
            disk.FreeBytes = reading.FreeBytes;
            disk.FileSystem = reading.FileSystem;
            disk.Name = MountPaths.DisplayName(group.MountPoint, reading.Label);
            if (reading.IsNetwork)
            {
                disk.Kind = DiskKind.Share;
            }
        }

        await MarkExternalAsync(disk, cancellationToken).ConfigureAwait(false);
        await ReadSmartAsync(disk, cancellationToken).ConfigureAwait(false);
        await CheckWritableAsync(disk, cancellationToken).ConfigureAwait(false);
        return disk;
    }

    // Each library folder on a disk that was read, and is there, is asked whether Jellyfin's user may write to it.
    private async Task CheckWritableAsync(DiskInfo disk, CancellationToken cancellationToken)
    {
        if (disk.State != DiskState.Available)
        {
            return;
        }

        var comparer = StringComparer.FromComparison(MountPaths.Comparison);
        var missing = new HashSet<string>(disk.MissingFolders, comparer);
        foreach (var path in disk.Folders.Where(f => !(f.Server && JellyfinFolders.ReadOnly(f.Library))).Select(f => f.Path).Where(p => !missing.Contains(p)).Distinct(comparer).ToList())
        {
            var folder = path;
            var writable = await _probe.RunAsync("write:" + folder, () => _mounts.CanWrite(folder), ProbeTimeout, cancellationToken).ConfigureAwait(false);
            if (writable.Completed && !writable.Value)
            {
                disk.ReadOnlyFolders.Add(folder);
            }
        }
    }

    // A disk read as local is asked whether it is plugged in from outside, such as over USB.
    private async Task MarkExternalAsync(DiskInfo disk, CancellationToken cancellationToken)
    {
        if (disk.Kind != DiskKind.Local || disk.State != DiskState.Available)
        {
            return;
        }

        var mountPoint = disk.MountPoint;
        var external = await _probe.RunAsync("external:" + mountPoint, () => _inventory.IsExternal(mountPoint), ProbeTimeout, cancellationToken).ConfigureAwait(false);
        if (external.Value)
        {
            disk.Kind = DiskKind.External;
        }
    }

    // A share has no SMART data of its own, so only local disks are asked.
    private async Task ReadSmartAsync(DiskInfo disk, CancellationToken cancellationToken)
    {
        if (disk.Kind == DiskKind.Share || disk.State != DiskState.Available)
        {
            return;
        }

        var smart = await _probe.RunAsync("smart:" + disk.MountPoint, () => _smart.Read(disk.MountPoint), ProbeTimeout * 2, cancellationToken).ConfigureAwait(false);
        if (smart.Value is { } health)
        {
            disk.Health = health.Health;
            disk.TemperatureCelsius = health.TemperatureCelsius;
            disk.PowerOnHours = health.PowerOnHours;
            disk.WearPercent = health.WearPercent;
        }
    }
}
