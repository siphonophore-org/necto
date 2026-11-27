using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Necto.Configuration;

/// <summary>
/// Marks the JSON options of a copy kept on the server, so <see cref="KeptSecretConverter"/> writes the stored value.
/// It converts only its own type, which nothing serializes. Options that carry it never share cached metadata with
/// other options, so the mark reaches every converter.
/// </summary>
internal sealed class ServerCopyMarker : JsonConverter<ServerCopyMarker>
{
    /// <inheritdoc />
    public override ServerCopyMarker Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ServerCopyMarker value, JsonSerializerOptions options)
        => throw new NotSupportedException();

    /// <summary>
    /// Works out whether JSON options are a server copy's.
    /// </summary>
    /// <param name="options">The options a converter was given.</param>
    /// <returns><c>true</c> when they carry the mark.</returns>
    public static bool Marks(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var converter in options.Converters)
        {
            if (converter is ServerCopyMarker)
            {
                return true;
            }
        }

        return false;
    }
}
