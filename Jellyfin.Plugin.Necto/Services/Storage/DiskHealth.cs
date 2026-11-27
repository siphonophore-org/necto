namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What a disk's SMART data says of its condition.
/// </summary>
public enum DiskHealth
{
    /// <summary>The disk reports no SMART data the plugin can read, as shares and many USB enclosures do.</summary>
    Unknown = 0,

    /// <summary>The disk's SMART data shows no failure.</summary>
    Good = 1,

    /// <summary>The disk's SMART data predicts it will fail.</summary>
    Failing = 2
}
