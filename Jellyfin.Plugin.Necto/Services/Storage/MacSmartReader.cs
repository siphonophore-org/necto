using System;
using System.Runtime.InteropServices;
using System.Text;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads SMART data on macOS from C#, the way diskutil does: DiskArbitration finds the disk behind a mount point, the
/// IOKit registry leads up to the NVMe device under it, and Apple's NVMe SMART plugin reads its health log. Disks
/// that are not NVMe, such as SATA and USB disks, report no reading.
/// </summary>
public sealed class MacSmartReader : ISmartReader
{
    // The NVMe SMART user client type and interface, and IOKit's plugin interface, from IOKit's headers.
    private static readonly byte[] SmartUserClientType = { 0xAA, 0x0F, 0xA6, 0xF9, 0xC2, 0xD6, 0x45, 0x7F, 0xB1, 0x0B, 0x59, 0xA1, 0x32, 0x53, 0x29, 0x2F };
    private static readonly byte[] SmartInterface = { 0xCC, 0xD1, 0xDB, 0x19, 0xFD, 0x9A, 0x4D, 0xAF, 0xBF, 0x95, 0x12, 0x45, 0x4B, 0x23, 0x0A, 0xB6 };
    private static readonly byte[] PlugInInterface = { 0xC2, 0x44, 0xE8, 0x58, 0x10, 0x9C, 0x11, 0xD4, 0x91, 0xD4, 0x00, 0x50, 0xE4, 0xC6, 0x42, 0x6F };

    // The registry property an NVMe disk that answers SMART requests carries.
    private const string SmartCapableKey = "NVMe SMART Capable";

    // How far up the registry the walk from a volume to its disk may go.
    private const int MaxDepth = 32;

    // In a COM style interface, the vtable starts with a reserved slot, QueryInterface, AddRef, and Release.
    private const int QueryInterfaceSlot = 1;
    private const int ReleaseSlot = 3;

    // IONVMeSMARTInterface puts SMARTReadData after the four IUnknown slots and two 16 bit version fields, padded to 8.
    private const int SmartReadDataOffset = 40;

    private readonly ILogger<MacSmartReader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacSmartReader"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacSmartReader(ILogger<MacSmartReader> logger)
    {
        _logger = logger;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int QueryInterfaceFunction(IntPtr self, Uuid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ReleaseFunction(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SmartReadDataFunction(IntPtr self, IntPtr data);

    /// <inheritdoc />
    public SmartReading? Read(string mountPoint)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var bsdName = BsdNameOf(mountPoint);
        if (bsdName is null)
        {
            return null;
        }

        var device = FindSmartDevice(bsdName);
        if (device == 0)
        {
            _logger.LogDebug("No NVMe SMART device under {MountPoint}", mountPoint);
            return null;
        }

        try
        {
            return ReadLog(device);
        }
        finally
        {
            _ = MacSmartNative.IOObjectRelease(device);
        }
    }

    // The BSD name of the volume mounted at a path, such as disk3s1s1, through DiskArbitration.
    private static string? BsdNameOf(string mountPoint)
    {
        var path = Encoding.UTF8.GetBytes(mountPoint);
        var url = MacSmartNative.CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, true);
        if (url == IntPtr.Zero)
        {
            return null;
        }

        var session = MacNativeMethods.DASessionCreate(IntPtr.Zero);
        try
        {
            var disk = session == IntPtr.Zero ? IntPtr.Zero : MacSmartNative.DADiskCreateFromVolumePath(IntPtr.Zero, session, url);
            if (disk == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var name = MacSmartNative.DADiskGetBSDName(disk);
                return name == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(name);
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

    // Walks up the IOService plane from the volume's media to the first entry that answers NVMe SMART requests.
    private static uint FindSmartDevice(string bsdName)
    {
        var matching = MacSmartNative.IOBSDNameMatching(0, 0, Encoding.UTF8.GetBytes(bsdName + "\0"));
        if (matching == IntPtr.Zero)
        {
            return 0;
        }

        // IOServiceGetMatchingService takes ownership of the matching dictionary.
        var entry = MacSmartNative.IOServiceGetMatchingService(0, matching);
        var plane = Encoding.UTF8.GetBytes("IOService\0");
        for (var depth = 0; entry != 0 && depth < MaxDepth; depth++)
        {
            if (IsSmartCapable(entry))
            {
                return entry;
            }

            var result = MacSmartNative.IORegistryEntryGetParentEntry(entry, plane, out var parent);
            _ = MacSmartNative.IOObjectRelease(entry);
            entry = result == 0 ? parent : 0;
        }

        if (entry != 0)
        {
            _ = MacSmartNative.IOObjectRelease(entry);
        }

        return 0;
    }

    private static bool IsSmartCapable(uint entry)
    {
        var key = MacSmartNative.CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(SmartCapableKey + "\0"), 0x08000100);
        try
        {
            var value = MacSmartNative.IORegistryEntryCreateCFProperty(entry, key, IntPtr.Zero, 0);
            if (value == IntPtr.Zero)
            {
                return false;
            }

            var capable = MacSmartNative.CFGetTypeID(value) == MacSmartNative.CFBooleanGetTypeID() && MacSmartNative.CFBooleanGetValue(value);
            MacNativeMethods.CFRelease(value);
            return capable;
        }
        finally
        {
            MacNativeMethods.CFRelease(key);
        }
    }

    // Loads Apple's NVMe SMART plugin for the device, asks it for the SMART interface, and reads the health log.
    private SmartReading? ReadLog(uint device)
    {
        var pluginType = Uuid.Constant(SmartUserClientType);
        var plugInInterface = Uuid.Constant(PlugInInterface);
        var status = MacSmartNative.IOCreatePlugInInterfaceForService(device, pluginType, plugInInterface, out var plugin, out _);
        if (status != 0 || plugin == IntPtr.Zero)
        {
            _logger.LogDebug("Could not load the NVMe SMART plugin, IOKit returned {Status}", status);
            return null;
        }

        var smart = IntPtr.Zero;
        var log = Marshal.AllocHGlobal(NvmeSmartLog.Size);
        try
        {
            var query = Slot<QueryInterfaceFunction>(plugin, QueryInterfaceSlot * IntPtr.Size);
            if (query(plugin, Uuid.FromBytes(SmartInterface), out smart) != 0 || smart == IntPtr.Zero)
            {
                return null;
            }

            var read = Slot<SmartReadDataFunction>(smart, SmartReadDataOffset);
            var result = read(smart, log);
            if (result != 0)
            {
                _logger.LogDebug("The NVMe SMART read returned {Status}", result);
                return null;
            }

            var bytes = new byte[NvmeSmartLog.Size];
            Marshal.Copy(log, bytes, 0, bytes.Length);
            return NvmeSmartLog.Parse(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(log);
            if (smart != IntPtr.Zero)
            {
                Slot<ReleaseFunction>(smart, ReleaseSlot * IntPtr.Size)(smart);
            }

            _ = MacSmartNative.IODestroyPlugInInterface(plugin);
        }
    }

    // A COM style interface pointer points to a pointer to its vtable, so a function sits at the vtable plus its offset.
    private static T Slot<T>(IntPtr instance, int offset)
        where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), offset));

    /// <summary>
    /// A CFUUIDBytes value, passed to QueryInterface by value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Uuid
    {
        private readonly ulong _low;
        private readonly ulong _high;

        private Uuid(byte[] bytes)
        {
            _low = BitConverter.ToUInt64(bytes, 0);
            _high = BitConverter.ToUInt64(bytes, 8);
        }

        public static Uuid FromBytes(byte[] bytes) => new(bytes);

        public static IntPtr Constant(byte[] b)
            => MacSmartNative.CFUUIDGetConstantUUIDWithBytes(IntPtr.Zero, b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
    }
}
