namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// The alerts a disk's SMART data can set off: failing, too hot, and worn out.
/// </summary>
public sealed class HealthAlerts
{
    /// <summary>Gets or sets a value indicating whether a disk whose SMART data predicts it will fail is recorded.</summary>
    public bool Failing { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a disk running past <see cref="HotCelsius"/> is recorded.</summary>
    public bool Hot { get; set; } = true;

    /// <summary>Gets or sets the temperature, in °C, past which a disk is too hot.</summary>
    public int HotCelsius { get; set; } = 60;

    /// <summary>Gets or sets a value indicating whether an SSD past <see cref="WornPercent"/> of its rated life is recorded.</summary>
    public bool Worn { get; set; } = true;

    /// <summary>Gets or sets the share of its rated life, from 0 to 100, past which an SSD is worn out.</summary>
    public int WornPercent { get; set; } = 90;
}
