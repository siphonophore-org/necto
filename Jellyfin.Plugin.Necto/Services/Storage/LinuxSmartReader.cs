using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads SMART data on Linux from udisks2 over the system D-Bus. udisks reads every disk's SMART data on its own and
/// lets any user read the result, so Jellyfin needs no root. A system without udisks, such as most containers,
/// reports no reading.
/// </summary>
public sealed class LinuxSmartReader : ISmartReader
{
    private readonly ILogger<LinuxSmartReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxSmartReader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LinuxSmartReader(ILogger<LinuxSmartReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public SmartReading? Read(string mountPoint)
    {
        var address = DBusAddress.System;
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        try
        {
            var objects = GetManagedObjectsAsync(address).GetAwaiter().GetResult();
            return UdisksSmart.Find(objects, MountPaths.Normalize(mountPoint));
        }
        catch (Exception ex) when (ex is DBusExceptionBase or System.IO.IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Could not read SMART data from udisks for {MountPoint}", mountPoint);
            return null;
        }
    }

    internal static async Task<Dictionary<string, Dictionary<string, Dictionary<string, object?>>>> GetManagedObjectsAsync(string address)
    {
        using var connection = new DBusConnection(address);
        await connection.ConnectAsync().ConfigureAwait(false);
        MessageBuffer message;
        using (var writer = connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader("org.freedesktop.UDisks2", "/org/freedesktop/UDisks2", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects", null, MessageFlags.None);
            message = writer.CreateMessage();
        }

        return await connection.CallMethodAsync(message, (Message reply, object? _) => ReadObjects(reply), null).ConfigureAwait(false);
    }

    // The reply is a{oa{sa{sv}}}: each object path, its interfaces, and each interface's properties.
    private static Dictionary<string, Dictionary<string, Dictionary<string, object?>>> ReadObjects(Message reply)
    {
        var reader = reply.GetBodyReader();
        var objects = new Dictionary<string, Dictionary<string, Dictionary<string, object?>>>(StringComparer.Ordinal);
        var objectsEnd = reader.ReadDictionaryStart();
        while (reader.HasNext(objectsEnd))
        {
            var path = reader.ReadObjectPathAsString();
            var interfaces = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
            var interfacesEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(interfacesEnd))
            {
                var name = reader.ReadString();
                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                var propertiesEnd = reader.ReadDictionaryStart();
                while (reader.HasNext(propertiesEnd))
                {
                    var key = reader.ReadString();
                    properties[key] = Plain(reader.ReadVariantValue());
                }

                interfaces[name] = properties;
            }

            objects[path] = interfaces;
        }

        return objects;
    }

    /// <summary>
    /// Converts a D-Bus value into the plain value <see cref="UdisksSmart"/> reads: unsigned and signed numbers as
    /// <see cref="ulong"/> and <see cref="long"/>, object paths as strings, byte arrays as byte arrays, and other
    /// arrays as lists.
    /// </summary>
    /// <param name="value">The D-Bus value.</param>
    /// <returns>The plain value, or <c>null</c> for a type udisks' SMART properties never use.</returns>
    public static object? Plain(VariantValue value) => value.Type switch
    {
        VariantValueType.Bool => value.GetBool(),
        VariantValueType.Byte => (ulong)value.GetByte(),
        VariantValueType.UInt16 => (ulong)value.GetUInt16(),
        VariantValueType.UInt32 => (ulong)value.GetUInt32(),
        VariantValueType.UInt64 => value.GetUInt64(),
        VariantValueType.Int16 => (long)value.GetInt16(),
        VariantValueType.Int32 => (long)value.GetInt32(),
        VariantValueType.Int64 => value.GetInt64(),
        VariantValueType.Double => value.GetDouble(),
        VariantValueType.String => value.GetString(),
        VariantValueType.ObjectPath => value.GetObjectPathAsString(),
        VariantValueType.Array when value.ItemType == VariantValueType.Byte => value.GetArray<byte>(),
        VariantValueType.Array => Enumerable.Range(0, value.Count).Select(i => Plain(value.GetItem(i))).ToList(),
        _ => null
    };
}
