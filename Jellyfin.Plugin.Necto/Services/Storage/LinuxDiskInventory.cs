using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Finds the disks on Linux with nothing mounted, blank or holding data, by reading files, with no tool and no root:
/// /sys/block for each disk, its size, model, and partitions, udev's records under /run/udev/data for the file system on
/// each, and the mount table and /proc/swaps for what is in use.
/// </summary>
public sealed class LinuxDiskInventory : IDiskInventory
{
    // Devices that are never a disk a person attaches: loop files, RAM disks, optical drives, and stacked devices.
    private static readonly string[] SkippedPrefixes = { "loop", "ram", "zram", "sr", "fd", "dm-", "md", "nbd" };

    // Parts of an eMMC chip that are not a disk to format: its boot areas and its replay protected memory.
    private static readonly string[] SkippedParts = { "boot0", "boot1", "rpmb" };

    private readonly string _sys;
    private readonly string _udev;
    private readonly string _mountInfo;
    private readonly string _swaps;
    private readonly string _fstab;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDiskInventory"/> class over the system's own files.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public LinuxDiskInventory(ILogger<LinuxDiskInventory> logger)
        : this("/sys", "/run/udev/data", "/proc/self/mountinfo", logger, "/proc/swaps", "/etc/fstab")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxDiskInventory"/> class over other folders, for tests.
    /// </summary>
    /// <param name="sys">Where sysfs is, normally /sys.</param>
    /// <param name="udev">Where udev keeps its device records, normally /run/udev/data.</param>
    /// <param name="mountInfo">The mount table, normally /proc/self/mountinfo.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="swaps">The swap table, normally /proc/swaps, or <c>null</c> for one beside the mount table.</param>
    /// <param name="fstab">The file system table, normally /etc/fstab, or <c>null</c> for one beside the mount table.</param>
    public LinuxDiskInventory(string sys, string udev, string mountInfo, ILogger logger, string? swaps = null, string? fstab = null)
    {
        _swaps = swaps ?? Path.Combine(Path.GetDirectoryName(mountInfo) ?? string.Empty, "swaps");
        _fstab = fstab ?? Path.Combine(Path.GetDirectoryName(mountInfo) ?? string.Empty, "fstab");
        _sys = sys;
        _udev = udev;
        _mountInfo = mountInfo;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnformatted() => GetUnmounted().Where(d => d.Blank).ToList();

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnmounted()
    {
        var block = Path.Combine(_sys, "block");
        // Without udev's records, as in most containers, a disk's file system cannot be known, so none is reported.
        if (!Directory.Exists(block) || !Directory.Exists(_udev))
        {
            return Array.Empty<RawDisk>();
        }

        var mounted = ReadMounted();
        var fstab = ReadFstab();
        var found = new List<RawDisk>();
        foreach (var disk in Directory.EnumerateDirectories(block).OrderBy(d => d, StringComparer.Ordinal))
        {
            // A disk unplugged while it is read is skipped, rather than failing the whole check.
            try
            {
                var raw = Read(disk, mounted, fstab);
                if (raw is not null)
                {
                    found.Add(raw);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "Skipped {Disk}, which could not be read", disk);
            }
        }

        _logger.LogDebug("Found {Count} disks with nothing mounted under {Path}", found.Count, block);
        return found;
    }

    // Reads one disk, or gives null when it is left out.
    private RawDisk? Read(string disk, HashSet<string> mounted, Dictionary<string, string> fstab)
    {
        var name = Path.GetFileName(disk);
        if (SkippedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) || SkippedParts.Any(p => name.EndsWith(p, StringComparison.Ordinal)))
        {
            return null;
        }

        var size = ReadLong(Path.Combine(disk, "size")) * 512;
        if (size <= 0)
        {
            return null;
        }

        // A disk is left out when anything on it is mounted or in use as swap, when something is stacked on it
        // such as dm-crypt, LVM, or RAID, or when udev has no record of it, since then it can't be read for sure.
        var partitions = Directory.EnumerateDirectories(disk).Where(d => File.Exists(Path.Combine(d, "partition"))).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var devices = new List<string> { disk };
        devices.AddRange(partitions);
        if (devices.Any(d => IsMounted(d, mounted) || HasHolders(d)) || devices.Any(d => !File.Exists(UdevRecord(d))))
        {
            return null;
        }

        // Blank is nothing at all: no partition, no partition table, and no file system. Anything else holds data,
        // even a partition udev can't identify, which may be an encrypted volume.
        var contents = devices.Select(Describe).Where(c => c is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        var blank = partitions.Count == 0 && contents.Count == 0 && !HasPartitionTable(disk);
        if (!blank && contents.Count == 0)
        {
            contents.Add(partitions.Count == 1 ? "1 partition" : partitions.Count + " partitions");
        }

        // A disk /etc/fstab mounts somewhere, such as a library disk that dropped off, says where.
        var mountsAt = devices.Select(d => FstabDir(d, fstab)).FirstOrDefault(d => d is not null);
        return new RawDisk("/dev/" + name, ReadModel(disk), size, blank ? null : contents, IsUsb(disk), mountsAt);
    }

    /// <inheritdoc />
    public bool IsExternal(string mountPoint)
    {
        if (string.IsNullOrEmpty(mountPoint) || !File.Exists(_mountInfo))
        {
            return false;
        }

        // The mount table's fifth field is the mount point, with spaces and other odd bytes written as \NNN, its third
        // the device number, and the field after its - separator's type the source, such as /dev/sdb1.
        var fields = File.ReadLines(_mountInfo)
            .Select(l => l.Split(' '))
            .LastOrDefault(f => f.Length > 4 && string.Equals(UnescapeMount(f[4]), mountPoint, StringComparison.Ordinal));
        if (fields is null)
        {
            return false;
        }

        // /sys/dev/block names each device by its number. Btrfs gives a made-up number, so its source names it instead.
        var device = Path.Combine(_sys, "dev", "block", fields[2]);
        var dash = Array.IndexOf(fields, "-");
        if (!Directory.Exists(device) && dash > 0 && dash + 2 < fields.Length && fields[dash + 2].StartsWith("/dev/", StringComparison.Ordinal))
        {
            device = Path.Combine(_sys, "class", "block", Path.GetFileName(fields[dash + 2]));
        }

        if (!Directory.Exists(device))
        {
            return false;
        }

        // A partition's disk is the folder it sits in.
        var resolved = new DirectoryInfo(device).ResolveLinkTarget(true)?.FullName ?? device;
        var disk = File.Exists(Path.Combine(resolved, "partition")) ? Path.GetDirectoryName(resolved) ?? resolved : resolved;
        return IsUsb(disk);
    }

    /// <summary>
    /// Reads a path from the mount table, where a space, tab, newline, or backslash is written as \ and three octal
    /// digits, such as \040 for a space.
    /// </summary>
    /// <param name="text">The path as the mount table writes it.</param>
    /// <returns>The path.</returns>
    public static string UnescapeMount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && IsOctal(text, i + 1))
            {
                builder.Append((char)Convert.ToInt32(text.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(string text, int start)
        => start + 3 <= text.Length && text.Substring(start, 3).All(c => c is >= '0' and <= '7');

    // A disk on USB: udev says so, or its place in the device tree runs through a USB controller.
    private bool IsUsb(string disk)
    {
        var record = UdevRecord(disk);
        if (File.Exists(record) && File.ReadLines(record).Any(l => l == "E:ID_BUS=usb" || l.StartsWith("E:ID_USB_DRIVER=", StringComparison.Ordinal)))
        {
            return true;
        }

        var resolved = new DirectoryInfo(disk).ResolveLinkTarget(true)?.FullName ?? disk;
        return resolved.Contains("/usb", StringComparison.Ordinal);
    }

    // /etc/fstab's entries by what they name, UUID=... or a device path, to the folder each mounts at.
    private Dictionary<string, string> ReadFstab()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_fstab))
        {
            return entries;
        }

        foreach (var line in File.ReadLines(_fstab))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 1 && !fields[0].StartsWith('#'))
            {
                entries[fields[0]] = UnescapeMount(fields[1]);
            }
        }

        return entries;
    }

    // The folder /etc/fstab mounts a device at, found by its file system's UUID or its device path.
    private string? FstabDir(string device, Dictionary<string, string> fstab)
    {
        var uuid = File.ReadLines(UdevRecord(device)).FirstOrDefault(l => l.StartsWith("E:ID_FS_UUID=", StringComparison.Ordinal))?["E:ID_FS_UUID=".Length..];
        if (!string.IsNullOrEmpty(uuid) && fstab.TryGetValue("UUID=" + uuid, out var byUuid))
        {
            return byUuid;
        }

        return fstab.TryGetValue("/dev/" + Path.GetFileName(device), out var byPath) ? byPath : null;
    }

    /// <summary>
    /// Tells whether /etc/fstab already mounts something at a folder, so a new disk is not given the same one.
    /// </summary>
    /// <param name="mountPoint">The folder.</param>
    /// <returns><c>true</c> when an entry mounts there.</returns>
    public bool HasFstabEntry(string mountPoint) => ReadFstab().Values.Any(d => string.Equals(MountPaths.Normalize(d), MountPaths.Normalize(mountPoint), StringComparison.Ordinal));

    // A device is in use when it is mounted or is active swap.
    private bool IsMounted(string device, HashSet<string> mounted)
    {
        var number = ReadText(Path.Combine(device, "dev"));
        return (number is not null && mounted.Contains(number)) || mounted.Contains("name:" + Path.GetFileName(device));
    }

    // What udev saw on a device: its file system's label and type, such as "Debian 13.3.0 (iso9660)", or the type alone.
    private string? Describe(string device)
    {
        string? type = null;
        string? label = null;
        foreach (var line in File.ReadLines(UdevRecord(device)))
        {
            if (line.StartsWith("E:ID_FS_TYPE=", StringComparison.Ordinal))
            {
                type = line["E:ID_FS_TYPE=".Length..];
            }
            else if (line.StartsWith("E:ID_FS_LABEL_ENC=", StringComparison.Ordinal))
            {
                label = Unescape(line["E:ID_FS_LABEL_ENC=".Length..]);
            }
        }

        if (string.IsNullOrEmpty(type))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(label) ? type : label.Trim() + " (" + type + ")";
    }

    /// <summary>
    /// Reads udev's encoded label, where a byte outside plain text is written as \xNN, such as \x20 for a space.
    /// </summary>
    /// <param name="text">The encoded label.</param>
    /// <returns>The label.</returns>
    public static string Unescape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 3 < text.Length && text[i + 1] == 'x'
                && byte.TryParse(text.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add(value);
                i += 3;
            }
            else
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(text[i].ToString()));
            }
        }

        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    // A device something is stacked on, such as dm-crypt, LVM, or RAID, lists it under holders.
    private static bool HasHolders(string device)
    {
        var holders = Path.Combine(device, "holders");
        return Directory.Exists(holders) && Directory.EnumerateFileSystemEntries(holders).Any();
    }

    // udev records a partition table even when the kernel lists no partition.
    private bool HasPartitionTable(string device)
        => File.ReadLines(UdevRecord(device)).Any(l => l.StartsWith("E:ID_PART_TABLE_TYPE=", StringComparison.Ordinal));

    private string UdevRecord(string device) => Path.Combine(_udev, "b" + (ReadText(Path.Combine(device, "dev")) ?? string.Empty));

    private HashSet<string> ReadMounted()
    {
        var mounted = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(_mountInfo))
        {
            return mounted;
        }

        // Each line's third field is the mounted device's major:minor number, and the field after its - separator's
        // type is the source, such as /dev/sdb1. Btrfs, ZFS, and bcachefs give a made-up number, so both are kept.
        foreach (var line in File.ReadLines(_mountInfo))
        {
            var fields = line.Split(' ');
            if (fields.Length > 2)
            {
                mounted.Add(fields[2]);
            }

            var dash = Array.IndexOf(fields, "-");
            if (dash > 0 && dash + 2 < fields.Length && fields[dash + 2].StartsWith("/dev/", StringComparison.Ordinal))
            {
                mounted.Add("name:" + Path.GetFileName(fields[dash + 2]));
            }
        }

        // Active swap is in use too, though it is not mounted. /proc/swaps names each by its device path.
        foreach (var number in ActiveSwap())
        {
            mounted.Add(number);
        }

        return mounted;
    }

    // The major:minor numbers of the block devices in use as swap.
    private IEnumerable<string> ActiveSwap()
    {
        if (!File.Exists(_swaps))
        {
            yield break;
        }

        foreach (var line in File.ReadLines(_swaps).Skip(1))
        {
            var path = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (path is null || !path.StartsWith("/dev/", StringComparison.Ordinal))
            {
                continue;
            }

            var number = ReadText(Path.Combine(_sys, "class", "block", Path.GetFileName(path), "dev"));
            if (number is not null)
            {
                yield return number;
            }
        }
    }

    private static string? ReadModel(string disk)
    {
        var vendor = ReadText(Path.Combine(disk, "device", "vendor"));
        var model = ReadText(Path.Combine(disk, "device", "model"));
        // SATA disks report ATA as their vendor, which says nothing about the disk.
        if (string.IsNullOrEmpty(vendor) || string.Equals(vendor, "ATA", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(model) ? null : model;
        }

        return string.IsNullOrEmpty(model) ? vendor : vendor + " " + model;
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long ReadLong(string path) => long.TryParse(ReadText(path), out var value) ? value : 0;
}
