using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Finds a mount point's SMART data in udisks2's object tree, as GetManagedObjects returns it: the filesystem mounted
/// there leads to its block device, the block device names its drive, and the drive carries SMART properties for ATA
/// or NVMe. Values are plain .NET values, as <see cref="LinuxSmartReader"/> converts them, so this is testable without
/// D-Bus: booleans, unsigned numbers as <see cref="ulong"/>, doubles, strings for strings and object paths, byte
/// arrays for arrays of bytes, and lists for other arrays.
/// </summary>
public static class UdisksSmart
{
    private const string BlockInterface = "org.freedesktop.UDisks2.Block";
    private const string FilesystemInterface = "org.freedesktop.UDisks2.Filesystem";
    private const string AtaInterface = "org.freedesktop.UDisks2.Drive.Ata";
    private const string NvmeInterface = "org.freedesktop.UDisks2.NVMe.Controller";

    /// <summary>
    /// Reads the SMART data of the drive a mount point sits on.
    /// </summary>
    /// <param name="objects">udisks2's objects, by path, then interface, then property.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <returns>The reading, or <c>null</c> when the drive has none, such as a share, an LVM volume, or a USB disk.</returns>
    public static SmartReading? Find(IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, object?>>> objects, string mountPoint)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var block = objects.Values.FirstOrDefault(o => o.TryGetValue(FilesystemInterface, out var fs) && MountPoints(fs).Contains(mountPoint, StringComparer.Ordinal));
        if (block is null || !block.TryGetValue(BlockInterface, out var blockProperties) || !blockProperties.TryGetValue("Drive", out var drivePath))
        {
            return null;
        }

        if (drivePath is not string path || !objects.TryGetValue(path, out var drive))
        {
            return null;
        }

        if (drive.TryGetValue(NvmeInterface, out var nvme))
        {
            return FromNvme(nvme);
        }

        return drive.TryGetValue(AtaInterface, out var ata) ? FromAta(ata) : null;
    }

    // An ATA disk counts only when SMART is on and udisks has read it at least once.
    private static SmartReading? FromAta(Dictionary<string, object?> ata)
    {
        if (!Bool(ata, "SmartSupported") || !Bool(ata, "SmartEnabled") || UInt(ata, "SmartUpdated") == 0)
        {
            return null;
        }

        var health = Bool(ata, "SmartFailing") ? DiskHealth.Failing : DiskHealth.Good;
        var kelvin = ata.TryGetValue("SmartTemperature", out var t) && t is double degrees ? degrees : 0;
        var seconds = UInt(ata, "SmartPowerOnSeconds");
        return new SmartReading(health, kelvin > 0 ? (int)Math.Round(kelvin - 273.15) : null, seconds > 0 ? (long)(seconds / 3600) : null);
    }

    // An NVMe disk's critical warnings list what is wrong, such as spare or temperature, and is empty when all is well.
    private static SmartReading? FromNvme(Dictionary<string, object?> nvme)
    {
        if (UInt(nvme, "SmartUpdated") == 0)
        {
            return null;
        }

        var warnings = nvme.TryGetValue("SmartCriticalWarning", out var w) && w is System.Collections.ICollection list ? list.Count : 0;
        var kelvin = UInt(nvme, "SmartTemperature");
        var hours = UInt(nvme, "SmartPowerOnHours");
        return new SmartReading(warnings > 0 ? DiskHealth.Failing : DiskHealth.Good, kelvin > 0 ? (int)kelvin - 273 : null, hours > 0 ? (long)hours : null);
    }

    // MountPoints is a list of NUL terminated byte strings.
    private static IEnumerable<string> MountPoints(Dictionary<string, object?> filesystem)
    {
        if (!filesystem.TryGetValue("MountPoints", out var points) || points is not IEnumerable<object?> list)
        {
            yield break;
        }

        foreach (var bytes in list.OfType<byte[]>())
        {
            var end = Array.IndexOf(bytes, (byte)0);
            yield return MountPaths.Normalize(Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end));
        }
    }

    private static bool Bool(Dictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) && value is true;

    private static ulong UInt(Dictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value switch
        {
            ulong number => number,
            long number when number > 0 => (ulong)number,
            _ => 0
        } : 0;
}
