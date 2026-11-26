using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads the SMART data of the disk behind a mount point. Calls may block, so callers run them through
/// <see cref="VolumeProbe"/>.
/// </summary>
public interface ISmartReader
{
    /// <summary>
    /// Reads the SMART data of the physical disk a mount point sits on.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <returns>The reading, or <c>null</c> when the disk reports none the plugin can read.</returns>
    SmartReading? Read(string mountPoint);
}
