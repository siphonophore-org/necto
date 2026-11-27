using System;
using System.Buffers.Binary;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads the 512 byte SMART / Health Information log NVMe disks return, as macOS and Windows hand it over. Pure, so
/// the layout is testable on any OS.
/// </summary>
public static class NvmeSmartLog
{
    /// <summary>The size of the log.</summary>
    public const int Size = 512;

    /// <summary>
    /// Reads the health, temperature, power-on hours, and wear from the log.
    /// </summary>
    /// <param name="log">The log.</param>
    /// <returns>The reading, or <c>null</c> when the log is too short.</returns>
    public static SmartReading? Parse(ReadOnlySpan<byte> log)
    {
        if (log.Length < Size)
        {
            return null;
        }

        // Any critical warning bit means the spare is low, the disk is too hot, its media is unreliable, it went read
        // only, or its backup power failed.
        var health = log[0] == 0 ? DiskHealth.Good : DiskHealth.Failing;
        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log[1..]);
        int? celsius = kelvin > 0 ? kelvin - 273 : null;
        var hours = BinaryPrimitives.ReadInt64LittleEndian(log[128..]);
        return new SmartReading(health, celsius, hours >= 0 ? hours : null, log[5]);
    }
}
