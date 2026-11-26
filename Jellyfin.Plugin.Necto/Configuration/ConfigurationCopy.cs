using System.Text.Json;

namespace Jellyfin.Plugin.Necto.Configuration;

/// <summary>
/// Copies a part of the configuration whole, so a caller holding it outside the configuration lock never reads a list
/// a save is changing, and never writes into the stored configuration by accident.
/// </summary>
public static class ConfigurationCopy
{
    /// <summary>
    /// Copies a value through JSON.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to copy.</param>
    /// <returns>An independent copy.</returns>
    public static T Of<T>(T value)
        where T : class, new()
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)) ?? new T();
}
