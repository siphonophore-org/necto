using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads the mounted volumes through .NET's <see cref="DriveInfo"/>, which lists them on Linux, macOS, and Windows.
/// </summary>
public sealed class DriveMountTable : IMountTable
{
    private readonly ILogger<DriveMountTable> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriveMountTable"/> class.
    /// </summary>
    /// <param name="logger">Logs a volume that cannot be read.</param>
    public DriveMountTable(ILogger<DriveMountTable> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetMountPoints()
    {
        try
        {
            return DriveInfo.GetDrives().Select(d => MountPaths.Normalize(d.Name)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list the mounted volumes");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public VolumeReading? Read(string mountPoint)
    {
        try
        {
            var drive = new DriveInfo(mountPoint);
            if (!drive.IsReady)
            {
                return null;
            }

            var label = OperatingSystem.IsWindows() ? drive.VolumeLabel : null;
            return new VolumeReading(drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat, drive.DriveType == DriveType.Network, label);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogDebug(ex, "Could not read the volume at {MountPoint}", mountPoint);
            return null;
        }
    }

    /// <inheritdoc />
    public bool CanWrite(string path)
    {
        // access asks the OS with Jellyfin's own user, including a read-only mount. Windows has no such call, and its
        // permissions are left to Jellyfin's own checks.
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        // A folder Jellyfin has not made yet, such as transcodes before the first transcode, has nothing to flag.
        return !Directory.Exists(path) || UnixNative.access(path, UnixNative.WriteOk) == 0;
    }

    /// <inheritdoc />
    public FolderStatus GetFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return FolderStatus.Missing;
            }

            return Directory.EnumerateFileSystemEntries(path).Any() ? FolderStatus.Present : FolderStatus.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not list the folder {Path}", path);
            return FolderStatus.Missing;
        }
    }
}
