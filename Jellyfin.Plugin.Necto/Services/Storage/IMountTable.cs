using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The OS's mounted volumes. Every call may block on a dead share, so callers run them through <see cref="VolumeProbe"/>.
/// </summary>
public interface IMountTable
{
    /// <summary>
    /// Lists the mount points the OS has, normalized.
    /// </summary>
    /// <returns>The mount points.</returns>
    IReadOnlyList<string> GetMountPoints();

    /// <summary>
    /// Reads a volume's size, free space, and file system.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <returns>The reading, or <c>null</c> when the volume cannot be read.</returns>
    VolumeReading? Read(string mountPoint);

    /// <summary>
    /// Tells whether a folder exists, and whether it holds anything.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <returns>What the folder holds.</returns>
    FolderStatus GetFolder(string path);

    /// <summary>
    /// Tells whether Jellyfin's user may write to a folder, so it can save NFO files and images next to the media,
    /// without writing anything, since a write would set off real-time monitoring.
    /// </summary>
    /// <param name="path">A folder that exists.</param>
    /// <returns><c>false</c> when it may not, <c>true</c> when it may or the OS can't say.</returns>
    bool CanWrite(string path) => true;
}
