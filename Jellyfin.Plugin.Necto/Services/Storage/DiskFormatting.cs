using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Formats a disk with nothing mounted and mounts it, then adds it to a library when asked. Only a disk the inventory
/// lists at that moment is touched: never a mounted disk, the system disk, or one something is stacked on. A disk
/// that holds data is erased only when the request asks for it. One format runs at a time.
/// </summary>
public sealed class DiskFormatting : IDisposable
{
    /// <summary>How long a format may take, which is a quick format of a large disk.</summary>
    public static readonly TimeSpan FormatTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait for a new volume to show up when the OS mounts it on its own.</summary>
    public static readonly TimeSpan MountWait = TimeSpan.FromSeconds(30);

    private readonly IDiskInventory _inventory;
    private readonly IDiskFormatter _formatter;
    private readonly IMountTable _mounts;
    private readonly VolumeProbe _probe;
    private readonly LibraryFolders _libraries;
    private readonly StorageActivity _activity;
    private readonly StorageService _storage;
    private readonly ILogger<DiskFormatting> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskFormatting"/> class.
    /// </summary>
    /// <param name="inventory">Lists the unformatted disks.</param>
    /// <param name="formatter">Formats a disk the way the OS does.</param>
    /// <param name="mounts">Reads the mounted volumes, to find one the OS mounted on its own.</param>
    /// <param name="probe">Runs the format with a time limit.</param>
    /// <param name="libraries">Adds the new disk to a library.</param>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="storage">Checks the disks again afterwards.</param>
    /// <param name="logger">The logger.</param>
    public DiskFormatting(
        IDiskInventory inventory,
        IDiskFormatter formatter,
        IMountTable mounts,
        VolumeProbe probe,
        LibraryFolders libraries,
        StorageActivity activity,
        StorageService storage,
        ILogger<DiskFormatting> logger)
    {
        _inventory = inventory;
        _formatter = formatter;
        _mounts = mounts;
        _probe = probe;
        _libraries = libraries;
        _activity = activity;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Gets what formatting offers on this server.
    /// </summary>
    public FormatOptions Options => new() { FileSystems = _formatter.FileSystems, MountRoot = _formatter.MountRoot, DashesForSpaces = _formatter.DashesForSpaces };

    /// <summary>
    /// Formats a disk and mounts it, then adds it to a library when one is named.
    /// </summary>
    /// <param name="request">The disk, its new name and file system, and the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What came of it.</returns>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The name is checked by FormatNames.IsValid first, which allows no path separators or dots.")]
    public async Task<FormatOutcome> FormatAsync(FormatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!FormatNames.IsValid(name))
        {
            return new FormatOutcome(false, ErrorKey: "ErrorFormatName");
        }

        if (!_formatter.FileSystems.Contains(request.FileSystem, StringComparer.Ordinal))
        {
            return new FormatOutcome(false, ErrorKey: "ErrorFormatFileSystem");
        }

        if (!string.IsNullOrEmpty(request.Library) && !_libraries.Exists(request.Library))
        {
            return new FormatOutcome(false, ErrorKey: "ErrorFormatLibrary");
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new FormatOutcome(false, ErrorKey: "ErrorFormatBusy");
        }

        // Once a format starts it runs to the end, even when the request is dropped, so the gate stays held while the
        // disk is being erased and the result is still recorded.
        cancellationToken = CancellationToken.None;

        try
        {
            // The disk must have nothing mounted right now, not only when the page was drawn, and a disk that holds
            // data is erased only when the request says so.
            var listed = await _probe.RunAsync("inventory", _inventory.GetUnmounted, StorageService.ProbeTimeout * 3, cancellationToken).ConfigureAwait(false);
            var disk = (listed.Value ?? Array.Empty<RawDisk>()).FirstOrDefault(d => string.Equals(d.Device, request.Device, StringComparison.Ordinal));
            if (disk is null)
            {
                return new FormatOutcome(false, ErrorKey: "ErrorFormatGone");
            }

            if (!disk.Blank && !request.Erase)
            {
                return new FormatOutcome(false, ErrorKey: "ErrorFormatHasData");
            }

            // A disk /etc/fstab mounts where a library expects its disk is that library's disk, dropped off, and is
            // mounted again rather than erased.
            var libraryMounts = LibraryMountPoints();
            if (disk.MountsAt is { } mountsAt && libraryMounts.Contains(MountPaths.Normalize(mountsAt)))
            {
                return new FormatOutcome(false, ErrorKey: "ErrorFormatLibraryDisk");
            }

            // The new disk's folder must be free: no files in it, no library expecting a disk there, and no other
            // fstab entry mounting there.
            var target = _formatter.MountRoot is { } root ? FormatNames.MountPoint(root, name, _formatter.DashesForSpaces) : null;
            if (target is not null && ((System.IO.Directory.Exists(target) && System.IO.Directory.EnumerateFileSystemEntries(target).Any())
                || libraryMounts.Contains(MountPaths.Normalize(target)) || _inventory.HasFstabEntry(target)))
            {
                return new FormatOutcome(false, ErrorKey: "ErrorFormatTaken");
            }

            var before = new HashSet<string>(_mounts.GetMountPoints(), StringComparer.FromComparison(MountPaths.Comparison));
            _logger.LogInformation("Formatting {Device} as {FileSystem} named {Name}", disk.Device, request.FileSystem, name);
            var run = await _probe.RunAsync("format:" + disk.Device, () => _formatter.Format(disk, name, request.FileSystem), FormatTimeout, cancellationToken).ConfigureAwait(false);
            var result = run.Completed && run.Value is not null ? run.Value : FormatResult.Failed("The format did not finish in time");
            if (!result.Formatted)
            {
                _activity.NotFormatted(disk, result.Error ?? string.Empty);
                return new FormatOutcome(false, Error: result.Error);
            }

            var mountPoint = result.MountPoint is { } known ? MountPaths.Normalize(known) : await WaitForNewMountAsync(before, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Formatted {Device} and mounted it at {MountPoint}", disk.Device, mountPoint ?? "an unknown folder");

            string? library = null;
            string? libraryError = null;
            if (mountPoint is not null && !string.IsNullOrEmpty(request.Library))
            {
                library = _libraries.TryAdd(request.Library, mountPoint) ? request.Library : null;
                libraryError = library is null ? "ErrorAddToLibrary" : null;
            }

            _activity.Formatted(disk, request.FileSystem, mountPoint, library);
            await _storage.CheckAsync(cancellationToken).ConfigureAwait(false);
            return new FormatOutcome(true, mountPoint, libraryError);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    // The mount points of the disks the libraries use, from the last check, missing ones included.
    private HashSet<string> LibraryMountPoints()
        => new(_storage.Known.Where(d => d.InUse && !string.IsNullOrEmpty(d.MountPoint)).Select(d => d.MountPoint), StringComparer.FromComparison(MountPaths.Comparison));

    // A volume the OS mounted on its own, such as macOS in /Volumes, is the mount point that was not there before.
    private async Task<string?> WaitForNewMountAsync(HashSet<string> before, CancellationToken cancellationToken)
    {
        for (var waited = TimeSpan.Zero; waited < MountWait; waited += TimeSpan.FromSeconds(1))
        {
            var added = _mounts.GetMountPoints().FirstOrDefault(m => !before.Contains(m));
            if (added is not null)
            {
                return added;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
