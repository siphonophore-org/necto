using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The IOKit, DiskArbitration, and CoreFoundation calls <see cref="MacSmartReader"/> uses to reach a disk's SMART
/// data. Only used on macOS.
/// </summary>
internal static class MacSmartNative
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string DiskArbitration = "/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration";

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] path, nint length, [MarshalAs(UnmanagedType.U1)] bool isDirectory);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] text, uint encoding);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CFBooleanGetValue(IntPtr value);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFUUIDGetConstantUUIDWithBytes(IntPtr allocator, byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7, byte b8, byte b9, byte b10, byte b11, byte b12, byte b13, byte b14, byte b15);

    [DllImport(DiskArbitration)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr DADiskCreateFromVolumePath(IntPtr allocator, IntPtr session, IntPtr path);

    [DllImport(DiskArbitration)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr DADiskGetBSDName(IntPtr disk);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr IOBSDNameMatching(uint mainPort, uint options, byte[] bsdName);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int IORegistryEntryGetParentEntry(uint entry, byte[] plane, out uint parent);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int IOObjectRelease(uint entry);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int IOCreatePlugInInterfaceForService(uint service, IntPtr pluginType, IntPtr interfaceType, out IntPtr plugin, out int score);

    [DllImport(IOKit)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int IODestroyPlugInInterface(IntPtr plugin);
}
