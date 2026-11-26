namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What the last check saw of one disk, so the next check can tell what changed.
/// </summary>
public sealed class DiskMemory
{
    /// <summary>Gets or sets whether the disk could be read.</summary>
    public DiskState State { get; set; }

    /// <summary>Gets or sets how full the disk was against its stops.</summary>
    public SpaceLevel Level { get; set; }

    /// <summary>Gets or sets what the disk's SMART data said of its condition.</summary>
    public DiskHealth Health { get; set; }

    /// <summary>Gets or sets whether the disk was inside the server, plugged in, or a share, when it was last read.</summary>
    public DiskKind Kind { get; set; }

    /// <summary>Gets or sets whether the disk was too hot.</summary>
    public bool Hot { get; set; }

    /// <summary>Gets or sets whether the SSD was worn out.</summary>
    public bool Worn { get; set; }
}
