namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// A change in a disk between two checks that the activity log records.
/// </summary>
public enum DiskEventKind
{
    /// <summary>The disk left its mount point.</summary>
    WentMissing = 0,

    /// <summary>The disk became unresponsive.</summary>
    BecameUnresponsive = 1,

    /// <summary>A missing or unresponsive disk can be read again.</summary>
    Returned = 2,

    /// <summary>The disk passed the Warning stop.</summary>
    Low = 3,

    /// <summary>The disk passed the Error stop.</summary>
    Full = 4,

    /// <summary>The disk's SMART data started predicting it will fail.</summary>
    Failing = 5,

    /// <summary>The disk ran past the Too hot alert.</summary>
    Hot = 6,

    /// <summary>The SSD passed the Worn out alert.</summary>
    Worn = 7
}
