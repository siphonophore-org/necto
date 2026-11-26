namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// What a stop's value counts.
/// </summary>
public enum SpaceUnit
{
    /// <summary>The share of the disk in use, from 0 to 100.</summary>
    Percent = 0,

    /// <summary>The free space left, in gigabytes.</summary>
    Gigabytes = 1
}
