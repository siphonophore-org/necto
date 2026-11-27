namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// One disk's own full disk alerts, used in place of the global ones.
/// </summary>
public sealed class DiskOverride
{
    /// <summary>Gets or sets the mount point of the disk.</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the disk's Warning alert.</summary>
    public SpaceStop WarningStop { get; set; } = new() { Value = 90 };

    /// <summary>Gets or sets the disk's Error alert.</summary>
    public SpaceStop ErrorStop { get; set; } = new() { Value = 95 };
}
