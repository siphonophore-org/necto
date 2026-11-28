using System;
using System.Collections.Generic;

using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// One disk or share the libraries use, as the Storage tab shows it.
/// </summary>
public sealed class DiskInfo
{
    /// <summary>Gets or sets the mount point, which identifies the disk.</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the device the OS names the disk by, such as /dev/sdb or disk4, for a disk with no mount point.</summary>
    public string? Device { get; set; }

    /// <summary>Gets or sets the disk's model, when the OS reports one.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets the name shown for the disk.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the disk can be read.</summary>
    public DiskState State { get; set; }

    /// <summary>Gets or sets a value indicating whether any library has a folder on this disk.</summary>
    public bool InUse { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the OS runs from this disk.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Gets or sets whether the disk is local or a share.</summary>
    public DiskKind Kind { get; set; }

    /// <summary>Gets or sets the file system, when the disk is mounted.</summary>
    public string? FileSystem { get; set; }

    /// <summary>Gets or sets the disk's size, when it is mounted.</summary>
    public long? TotalBytes { get; set; }

    /// <summary>Gets or sets the free space, when the disk is mounted.</summary>
    public long? FreeBytes { get; set; }

    /// <summary>Gets or sets what the disk's SMART data says of its condition.</summary>
    public DiskHealth Health { get; set; }

    /// <summary>Gets or sets the disk's temperature, when its SMART data reports one.</summary>
    public int? TemperatureCelsius { get; set; }

    /// <summary>Gets or sets how long the disk has been powered on, when its SMART data reports it.</summary>
    public long? PowerOnHours { get; set; }

    /// <summary>Gets or sets how much of its rated life an SSD has used, when it reports it.</summary>
    public int? WearPercent { get; set; }

    /// <summary>Gets or sets the folder /etc/fstab mounts a disk that is not mounted at, when it names one.</summary>
    public string? MountsAt { get; set; }

    /// <summary>Gets or sets what is on a disk that is not mounted, such as "Debian 13 (iso9660)".</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Serialized to the dashboard")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Serialized to the dashboard")]
    public List<string> Contents { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the disk runs past the Too hot alert.</summary>
    public bool Hot { get; set; }

    /// <summary>Gets or sets a value indicating whether the SSD is past the Worn out alert.</summary>
    public bool Worn { get; set; }

    /// <summary>Gets or sets how full the disk is against its stops.</summary>
    public SpaceLevel Level { get; set; }

    /// <summary>Gets or sets the share in use at which the Warning stop is reached, when it is on and the disk was read.</summary>
    public double? WarningPercent { get; set; }

    /// <summary>Gets or sets the share in use at which the Error stop is reached, when it is on and the disk was read.</summary>
    public double? ErrorPercent { get; set; }

    /// <summary>Gets or sets the Warning stop as set, when it is on and the disk was read.</summary>
    public SpaceStop? WarningStop { get; set; }

    /// <summary>Gets or sets the Error stop as set, when it is on and the disk was read.</summary>
    public SpaceStop? ErrorStop { get; set; }

    /// <summary>Gets or sets where the disk is mounted from, set by hand or seen while it was mounted, when known.</summary>
    public string? ShareAddress { get; set; }

    /// <summary>Gets or sets a value indicating whether the disk's address can be set by hand, so it can be remounted.</summary>
    public bool TakesAddress { get; set; }

    /// <summary>Gets or sets a value indicating whether the disk uses its own alerts instead of the global ones.</summary>
    public bool OwnAlerts { get; set; }

    /// <summary>Gets or sets a value indicating whether Jellyfin skips every folder of this missing disk in a scan on its own.</summary>
    public bool ScanSafe { get; set; } = true;

    /// <summary>Gets or sets when the disk was first seen missing or unresponsive, in UTC.</summary>
    public DateTime? MissingSinceUtc { get; set; }

    /// <summary>Gets or sets the libraries with a folder on the disk.</summary>
    public List<DiskLibrary> Libraries { get; set; } = new();

    /// <summary>Gets or sets the library folders on the disk.</summary>
    public List<DiskFolder> Folders { get; set; } = new();

    /// <summary>Gets or sets the library folders that are gone from the disk while it is mounted.</summary>
    public List<string> MissingFolders { get; set; } = new();

    /// <summary>Gets or sets the library folders on the disk Jellyfin's user may not write to, so metadata saved next to media fails.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Serialized to the dashboard")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Serialized to the dashboard")]
    public List<string> ReadOnlyFolders { get; set; } = new();

    /// <summary>Gets or sets the keys of the disk's notices an administrator dismissed.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Serialized to the dashboard")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Serialized to the dashboard")]
    public List<string> DismissedNotices { get; set; } = new();
}
