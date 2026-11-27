namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Whether a disk or share can be read.
/// </summary>
public enum DiskState
{
    /// <summary>The disk is mounted where the libraries expect it, and can be read.</summary>
    Available = 0,

    /// <summary>The disk is not mounted, or another disk is at its mount point.</summary>
    Missing = 1,

    /// <summary>The disk did not answer in time, as a dead share does.</summary>
    Unresponsive = 2,

    /// <summary>The disk holds no file system, so it has no mount point and no library can use it.</summary>
    Unformatted = 3,

    /// <summary>The disk holds data but nothing on it is mounted, so no library can use it until it is mounted or erased.</summary>
    NotMounted = 4
}
