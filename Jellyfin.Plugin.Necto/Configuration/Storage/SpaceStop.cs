namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// A point at which a disk counts as low on space or full.
/// </summary>
public sealed class SpaceStop
{
    /// <summary>Gets or sets a value indicating whether the stop is checked.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the stop: a share in use for <see cref="SpaceUnit.Percent"/>, or the free space left for <see cref="SpaceUnit.Gigabytes"/>.</summary>
    public double Value { get; set; }

    /// <summary>Gets or sets what <see cref="Value"/> counts.</summary>
    public SpaceUnit Unit { get; set; } = SpaceUnit.Percent;
}
