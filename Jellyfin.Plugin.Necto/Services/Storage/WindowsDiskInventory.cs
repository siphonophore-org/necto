using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Finds unformatted disks on Windows by asking each physical disk, \\.\PhysicalDrive0 and on, for its partition
/// layout, size, and model, called directly from C#. Reading a layout needs administrator rights, which a Jellyfin
/// service running as LocalSystem has. Without them no disk is reported.
/// </summary>
public sealed class WindowsDiskInventory : IDiskInventory
{
    // Windows numbers physical disks from 0 with gaps where a disk was removed, so every number up to this is tried.
    private const int MaxDisks = 64;

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsDiskInventory"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WindowsDiskInventory(ILogger<WindowsDiskInventory> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnformatted()
    {
        var found = new List<RawDisk>();
        if (!OperatingSystem.IsWindows())
        {
            return found;
        }

        var denied = 0;
        for (var number = 0; number < MaxDisks; number++)
        {
            using var device = WindowsNativeMethods.CreateFileW(
                @"\\.\PhysicalDrive" + number.ToString(CultureInfo.InvariantCulture),
                WindowsNativeMethods.GenericRead,
                WindowsNativeMethods.ShareReadWrite,
                IntPtr.Zero,
                WindowsNativeMethods.OpenExisting,
                0,
                IntPtr.Zero);
            if (device.IsInvalid)
            {
                // Access denied means the disk exists but Jellyfin is not an administrator.
                if (System.Runtime.InteropServices.Marshal.GetLastWin32Error() == 5)
                {
                    denied++;
                }

                continue;
            }

            var layout = Query(device, WindowsNativeMethods.IoctlDiskGetDriveLayoutEx, null, 64 * 1024);
            if (layout is null || WindowsDiskLayout.IsEmpty(layout) != true)
            {
                continue;
            }

            var length = Query(device, WindowsNativeMethods.IoctlDiskGetLengthInfo, null, 8);
            var size = length is { Length: >= 8 } ? BinaryPrimitives.ReadInt64LittleEndian(length) : 0;
            if (size <= 0)
            {
                continue;
            }

            // STORAGE_PROPERTY_QUERY for the device property, as a standard query.
            var descriptor = Query(device, WindowsNativeMethods.IoctlStorageQueryProperty, new byte[12], 1024);
            var model = descriptor is null ? null : WindowsDiskLayout.Model(descriptor);
            var external = descriptor is not null && WindowsDiskLayout.IsExternal(descriptor);
            found.Add(new RawDisk("Disk " + number.ToString(CultureInfo.InvariantCulture), model, size, null, external));
        }

        if (denied > 0)
        {
            _logger.LogDebug("Could not read {Count} disks because Jellyfin is not running as an administrator", denied);
        }

        return found;
    }

    /// <inheritdoc />
    public bool IsExternal(string mountPoint)
    {
        // Only a drive letter root has a volume to ask, such as E:\, and a network path is never external.
        if (!OperatingSystem.IsWindows() || mountPoint is null || mountPoint.Length < 2 || mountPoint.Length > 3 || mountPoint[1] != ':')
        {
            return false;
        }

        using var volume = WindowsNativeMethods.CreateFileW(@"\\.\" + mountPoint[..2], 0, WindowsNativeMethods.ShareReadWrite, IntPtr.Zero, WindowsNativeMethods.OpenExisting, 0, IntPtr.Zero);
        if (volume.IsInvalid)
        {
            return false;
        }

        var descriptor = Query(volume, WindowsNativeMethods.IoctlStorageQueryProperty, new byte[12], 1024);
        return descriptor is not null && WindowsDiskLayout.IsExternal(descriptor);
    }

    private static byte[]? Query(SafeFileHandle device, uint code, byte[]? input, int size)
    {
        var output = new byte[size];
        return WindowsNativeMethods.DeviceIoControl(device, code, input, input?.Length ?? 0, output, output.Length, out var returned, IntPtr.Zero)
            ? output.AsSpan(0, returned).ToArray()
            : null;
    }
}
