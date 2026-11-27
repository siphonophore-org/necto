using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// Single configuration object for the plugin. XML-serialized by Jellyfin.
/// </summary>
public class StorageConfiguration
{
    /// <summary>Gets or sets a value indicating whether Storage is on. While it is off, no disk is checked and no alert is sent.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the point at which a disk counts as low on space.</summary>
    public SpaceStop WarningStop { get; set; } = new() { Value = 90 };

    /// <summary>Gets or sets the point at which a disk counts as full.</summary>
    public SpaceStop ErrorStop { get; set; } = new() { Value = 95 };

    /// <summary>Gets or sets the alerts a disk's SMART data can set off.</summary>
    public HealthAlerts HealthAlerts { get; set; } = new();

    /// <summary>Gets or sets the disks with their own alerts, by mount point.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Set by XML serialization")]
    public List<DiskOverride> DiskOverrides { get; set; } = new();

    /// <summary>Gets or sets where each share is mounted from, set by hand, by mount point.</summary>
    [SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "List<T> required for XML serialization")]
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Set by XML serialization")]
    public List<ShareAddress> ShareAddresses { get; set; } = new();

    /// <summary>Gets or sets when a library scan is blocked while a disk is missing.</summary>
    public ScanGuardMode ScanGuard { get; set; } = ScanGuardMode.Affected;

    /// <summary>Gets or sets a value indicating whether a missing disk is mounted again at each check.</summary>
    public bool Remount { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether real-time monitoring ignores a missing disk's folders until it is back.</summary>
    public bool MuteMonitoring { get; set; } = true;
}
