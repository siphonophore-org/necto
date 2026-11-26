namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What one read of a disk's SMART data found.
/// </summary>
/// <param name="Health">Whether the disk predicts it will fail.</param>
/// <param name="TemperatureCelsius">The disk's temperature, when it reports one.</param>
/// <param name="PowerOnHours">How long the disk has been powered on, when it reports it.</param>
/// <param name="WearPercent">How much of its rated life the disk has used, for an SSD that reports it.</param>
public sealed record SmartReading(DiskHealth Health, int? TemperatureCelsius = null, long? PowerOnHours = null, int? WearPercent = null);
