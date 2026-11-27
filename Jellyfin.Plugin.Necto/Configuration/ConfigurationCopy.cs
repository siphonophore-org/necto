using System.Text.Json;

namespace Jellyfin.Plugin.Necto.Configuration;

/// <summary>
/// Copies a part of the configuration whole, so a caller holding it outside the configuration lock never reads a list
/// a save is changing, and never writes into the stored configuration by accident.
/// </summary>
public static class ConfigurationCopy
{
    /// <summary>
    /// Gets the JSON options of a copy kept on the server. They alone write a secret setting's stored value, where any
    /// other JSON writes the kept marker.
    /// </summary>
    public static JsonSerializerOptions Json { get; } = new() { Converters = { new ServerCopyMarker() } };

    /// <summary>
    /// Copies a value through JSON, secrets included.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to copy.</param>
    /// <returns>An independent copy.</returns>
    public static T Of<T>(T value)
        where T : class, new()
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json) ?? new T();
}
