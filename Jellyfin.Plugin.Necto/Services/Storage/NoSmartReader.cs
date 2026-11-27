using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The SMART reader on an OS the plugin has no reader for. Every disk's health is unknown.
/// </summary>
public sealed class NoSmartReader : ISmartReader
{
    /// <inheritdoc />
    public SmartReading? Read(string mountPoint) => null;
}
