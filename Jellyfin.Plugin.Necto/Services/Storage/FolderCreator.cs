using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Makes a folder directly on a mounted disk and adds it to a library when asked, such as Movies on a disk just
/// formatted. Only a disk the last check saw as available is used, and a folder that is already there is left alone.
/// </summary>
public sealed class FolderCreator
{
    private readonly StorageService _storage;
    private readonly IMountTable _mounts;
    private readonly LibraryFolders _libraries;
    private readonly StorageActivity _activity;
    private readonly ILogger<FolderCreator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderCreator"/> class.
    /// </summary>
    /// <param name="storage">Says which disks are available, and checks them again afterwards.</param>
    /// <param name="mounts">Says whether the disk is still mounted the moment the folder is made.</param>
    /// <param name="libraries">Adds the folder to a library.</param>
    /// <param name="activity">Writes the activity log.</param>
    /// <param name="logger">The logger.</param>
    public FolderCreator(StorageService storage, IMountTable mounts, LibraryFolders libraries, StorageActivity activity, ILogger<FolderCreator> logger)
    {
        _storage = storage;
        _mounts = mounts;
        _libraries = libraries;
        _activity = activity;
        _logger = logger;
    }

    /// <summary>
    /// Makes a folder on a disk and adds it to a library when one is named.
    /// </summary>
    /// <param name="request">The disk, the folder's name, and the library.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What came of it.</returns>
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "The mount point must be an available disk from the last check, and FolderNames.PathOn keeps the folder directly on it.")]
    public async Task<FolderOutcome> CreateAsync(FolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!FolderNames.IsValid(name))
        {
            return new FolderOutcome(false, ErrorKey: "ErrorFolderName");
        }

        if (!string.IsNullOrEmpty(request.Library) && !_libraries.Exists(request.Library))
        {
            return new FolderOutcome(false, ErrorKey: "ErrorFormatLibrary");
        }

        var disks = _storage.Last.Count > 0 ? _storage.Last : await _storage.CheckAsync(cancellationToken).ConfigureAwait(false);
        var disk = disks.FirstOrDefault(d => d.State == DiskState.Available && !string.IsNullOrEmpty(d.MountPoint)
            && string.Equals(d.MountPoint, MountPaths.Normalize(request.MountPoint ?? string.Empty), MountPaths.Comparison));
        var path = disk is null ? null : FolderNames.PathOn(disk.MountPoint, name);
        if (disk is null || path is null)
        {
            return new FolderOutcome(false, ErrorKey: "ErrorFolderDisk");
        }

        // The disk must still be mounted now, or the folder would land on the disk under it, often the system disk.
        if (!_mounts.GetMountPoints().Any(m => string.Equals(m, disk.MountPoint, MountPaths.Comparison)))
        {
            return new FolderOutcome(false, ErrorKey: "ErrorFolderDisk");
        }

        if (Directory.Exists(path) || File.Exists(path))
        {
            return new FolderOutcome(false, path, "ErrorFolderTaken");
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Most often Jellyfin's user may not write to the disk, such as one formatted by root.
            _logger.LogWarning(ex, "Could not make the folder {Path}", path);
            return new FolderOutcome(false, path, Error: ex.Message);
        }

        string? library = null;
        string? libraryError = null;
        if (!string.IsNullOrEmpty(request.Library))
        {
            library = _libraries.TryAdd(request.Library, path) ? request.Library : null;
            libraryError = library is null ? "ErrorAddToLibrary" : null;
        }

        _logger.LogInformation("Made the folder {Path}", path);
        _activity.FolderCreated(path, library);
        await _storage.CheckAsync(cancellationToken).ConfigureAwait(false);
        return new FolderOutcome(true, path, libraryError);
    }
}
