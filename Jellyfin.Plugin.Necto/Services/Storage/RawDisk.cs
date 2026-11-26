using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// A whole disk the OS sees, read below the level of file systems, with nothing on it mounted.
/// </summary>
/// <param name="Device">The device the OS names the disk by, such as /dev/sdb or disk4.</param>
/// <param name="Model">The disk's model, when the OS reports one.</param>
/// <param name="SizeBytes">The disk's size.</param>
/// <param name="Contents">What is on a disk that holds data, such as "Debian 13 (iso9660)", or <c>null</c> for a blank one.</param>
/// <param name="External">Whether the disk is plugged in from outside, such as over USB.</param>
/// <param name="MountsAt">The folder /etc/fstab mounts the disk at, when it names one, such as a library disk that dropped off.</param>
public sealed record RawDisk(string Device, string? Model, long SizeBytes, IReadOnlyList<string>? Contents = null, bool External = false, string? MountsAt = null)
{
    /// <summary>Gets a value indicating whether the disk has nothing on it: no partition, partition table, or file system.</summary>
    public bool Blank => Contents is null;
}
