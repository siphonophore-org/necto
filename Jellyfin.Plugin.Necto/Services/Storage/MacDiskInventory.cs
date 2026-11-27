using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Finds unformatted disks on macOS through DiskArbitration, the framework diskutil itself uses, called directly from
/// C#. The disk devices can only be read as root, but their descriptions can be read by anyone.
/// </summary>
public sealed partial class MacDiskInventory : IDiskInventory
{
    private readonly Func<IReadOnlyList<MacDiskDescription>> _describe;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacDiskInventory"/> class that asks DiskArbitration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacDiskInventory(ILogger<MacDiskInventory> logger)
        : this(null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MacDiskInventory"/> class with a stand-in for DiskArbitration, for tests.
    /// </summary>
    /// <param name="describe">Describes every whole disk, or <c>null</c> to ask DiskArbitration.</param>
    /// <param name="logger">The logger.</param>
    public MacDiskInventory(Func<IReadOnlyList<MacDiskDescription>>? describe, ILogger logger)
    {
        _logger = logger;
        _describe = describe ?? DescribeWholeDisks;
    }

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnformatted()
        => _describe()
            .Where(d => d.IsBlank)
            .Select(d => new RawDisk(d.Device, Clean(d.Model) ?? Clean(d.Name), d.SizeBytes, null, d.External))
            .ToList();

    /// <inheritdoc />
    public bool IsExternal(string mountPoint)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(mountPoint))
        {
            return false;
        }

        var path = Encoding.UTF8.GetBytes(mountPoint);
        var url = MacSmartNative.CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, true);
        if (url == IntPtr.Zero)
        {
            return false;
        }

        var session = MacNativeMethods.DASessionCreate(IntPtr.Zero);
        try
        {
            var disk = session == IntPtr.Zero ? IntPtr.Zero : MacSmartNative.DADiskCreateFromVolumePath(IntPtr.Zero, session, url);
            if (disk == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var description = MacNativeMethods.DADiskCopyDescription(disk);
                if (description == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    return IsExternal(
                        MacNativeMethods.GetString(description, MacNativeMethods.Key("kDADiskDescriptionDeviceProtocolKey")),
                        MacNativeMethods.GetBool(description, MacNativeMethods.Key("kDADiskDescriptionDeviceInternalKey")));
                }
                finally
                {
                    MacNativeMethods.CFRelease(description);
                }
            }
            finally
            {
                MacNativeMethods.CFRelease(disk);
            }
        }
        finally
        {
            if (session != IntPtr.Zero)
            {
                MacNativeMethods.CFRelease(session);
            }

            MacNativeMethods.CFRelease(url);
        }
    }

    /// <summary>
    /// Tells from DiskArbitration's description whether a disk is plugged in from outside. A disk image is not, since it
    /// is a file on a disk the server already has.
    /// </summary>
    /// <param name="protocol">The device protocol, such as USB, Thunderbolt, or Virtual Interface for a disk image.</param>
    /// <param name="isInternal">Whether DiskArbitration marks the device internal.</param>
    /// <returns><c>true</c> for an external disk.</returns>
    public static bool IsExternal(string? protocol, bool isInternal)
        => !isInternal && !string.Equals(protocol, "Virtual Interface", StringComparison.Ordinal) && !string.IsNullOrEmpty(protocol);

    /// <summary>
    /// Lists the whole disks among /dev's entries, such as disk4, leaving out their partitions, such as disk4s1.
    /// </summary>
    /// <param name="entries">The names in /dev.</param>
    /// <returns>The whole disks' names, in number order.</returns>
    public static IReadOnlyList<string> WholeDisks(IEnumerable<string> entries)
        => entries.Where(e => WholeDiskName().IsMatch(e))
            .OrderBy(e => int.Parse(e.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    // DiskArbitration names a disk's media with a trailing " Media", which says nothing.
    private static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return trimmed.EndsWith(" Media", StringComparison.Ordinal) ? trimmed[..^" Media".Length] : trimmed;
    }

    [GeneratedRegex("^disk[0-9]+$")]
    private static partial Regex WholeDiskName();

    private List<MacDiskDescription> DescribeWholeDisks()
    {
        var found = new List<MacDiskDescription>();
        var names = WholeDisks(Directory.EnumerateFileSystemEntries("/dev").Select(Path.GetFileName).OfType<string>());
        var session = MacNativeMethods.DASessionCreate(IntPtr.Zero);
        if (session == IntPtr.Zero)
        {
            _logger.LogDebug("DiskArbitration gave no session");
            return found;
        }

        try
        {
            var whole = MacNativeMethods.Key("kDADiskDescriptionMediaWholeKey");
            var leaf = MacNativeMethods.Key("kDADiskDescriptionMediaLeafKey");
            var content = MacNativeMethods.Key("kDADiskDescriptionMediaContentKey");
            var kind = MacNativeMethods.Key("kDADiskDescriptionVolumeKindKey");
            var mediaName = MacNativeMethods.Key("kDADiskDescriptionMediaNameKey");
            var model = MacNativeMethods.Key("kDADiskDescriptionDeviceModelKey");
            var size = MacNativeMethods.Key("kDADiskDescriptionMediaSizeKey");
            var protocol = MacNativeMethods.Key("kDADiskDescriptionDeviceProtocolKey");
            var isInternal = MacNativeMethods.Key("kDADiskDescriptionDeviceInternalKey");

            foreach (var name in names)
            {
                var bsd = Encoding.UTF8.GetBytes(name + "\0");
                var disk = MacNativeMethods.DADiskCreateFromBSDName(IntPtr.Zero, session, bsd);
                if (disk == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var description = MacNativeMethods.DADiskCopyDescription(disk);
                    if (description == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        found.Add(new MacDiskDescription(
                            name,
                            MacNativeMethods.GetBool(description, whole),
                            MacNativeMethods.GetBool(description, leaf),
                            MacNativeMethods.GetString(description, content),
                            MacNativeMethods.GetString(description, kind),
                            MacNativeMethods.GetString(description, mediaName),
                            MacNativeMethods.GetString(description, model),
                            MacNativeMethods.GetLong(description, size),
                            IsExternal(MacNativeMethods.GetString(description, protocol), MacNativeMethods.GetBool(description, isInternal))));
                    }
                    finally
                    {
                        MacNativeMethods.CFRelease(description);
                    }
                }
                finally
                {
                    MacNativeMethods.CFRelease(disk);
                }
            }
        }
        finally
        {
            MacNativeMethods.CFRelease(session);
        }

        _logger.LogDebug("DiskArbitration described {Count} whole disks", found.Count);
        return found;
    }
}
