using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The OS's whole disks, read below the level of file systems, so a disk with none can be found. Calls may block, so
/// callers run them through <see cref="VolumeProbe"/>.
/// </summary>
public interface IDiskInventory
{
    /// <summary>
    /// Lists the disks that hold no file system on the disk itself or on any of its partitions.
    /// </summary>
    /// <returns>The disks, or none when the OS cannot say for sure.</returns>
    IReadOnlyList<RawDisk> GetUnformatted();

    /// <summary>
    /// Lists every disk with nothing on it mounted: the blank ones, and the ones that hold data but are not mounted,
    /// such as a USB drive with an installer on it. A disk something is stacked on, such as LVM, is left out. An
    /// inventory that can't tell the second kind lists only blank disks.
    /// </summary>
    /// <returns>The disks, each with what is on it when it is not blank.</returns>
    IReadOnlyList<RawDisk> GetUnmounted() => GetUnformatted();

    /// <summary>
    /// Tells whether the disk behind a mount point is plugged in from outside, such as over USB or Thunderbolt.
    /// </summary>
    /// <param name="mountPoint">A mounted mount point.</param>
    /// <returns><c>true</c> for an external disk, <c>false</c> for one inside the server or when the OS can't say.</returns>
    bool IsExternal(string mountPoint) => false;

    /// <summary>
    /// Tells whether the OS already mounts some disk at a folder at boot, such as through /etc/fstab, so a new disk is
    /// not given the same folder.
    /// </summary>
    /// <param name="mountPoint">The folder.</param>
    /// <returns><c>true</c> when one does.</returns>
    bool HasFstabEntry(string mountPoint) => false;
}
