using System;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The libc, NetFS, and CoreFoundation calls <see cref="MacRemounter"/> uses to read what is mounted at a path and
/// to mount a share again. Only used on macOS.
/// </summary>
internal static class MacMountNative
{
    /// <summary>The size of struct statfs on 64 bit macOS.</summary>
    public const int StatfsSize = 2168;

    /// <summary>Where f_fstypename sits in struct statfs, 16 bytes long.</summary>
    public const int TypeNameOffset = 72;

    /// <summary>Where f_mntonname sits in struct statfs, 1024 bytes long.</summary>
    public const int MountedOnOffset = 88;

    /// <summary>Where f_mntfromname sits in struct statfs, 1024 bytes long.</summary>
    public const int MountedFromOffset = 1112;

    /// <summary>CFString's UTF-8 encoding.</summary>
    public const uint Utf8 = 0x08000100;

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string NetFS = "/System/Library/Frameworks/NetFS.framework/NetFS";

    /// <summary>
    /// Reads the file system a path is on. Apple silicon has one statfs, and Intel Macs name the 64 bit inode one
    /// statfs$INODE64.
    /// </summary>
    /// <param name="path">The path, NUL terminated UTF-8.</param>
    /// <param name="buffer">At least <see cref="StatfsSize"/> bytes.</param>
    /// <returns>0 when it worked.</returns>
    public static int Statfs(byte[] path, byte[] buffer)
        => RuntimeInformation.ProcessArchitecture == Architecture.X64 ? StatfsInode64(path, buffer) : StatfsArm64(path, buffer);

    [DllImport("libc", EntryPoint = "statfs", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int StatfsArm64(byte[] path, byte[] buffer);

    [DllImport("libc", EntryPoint = "statfs$INODE64", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int StatfsInode64(byte[] path, byte[] buffer);

    [DllImport(NetFS)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int NetFSMountURLSync(IntPtr url, IntPtr mountPath, IntPtr user, IntPtr password, IntPtr openOptions, IntPtr mountOptions, out IntPtr mountPoints);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] text, uint encoding);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFURLCreateWithString(IntPtr allocator, IntPtr text, IntPtr baseUrl);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte[] path, nint length, [MarshalAs(UnmanagedType.U1)] bool isDirectory);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    /// <summary>
    /// Gets the address of one of CoreFoundation's exported values, such as kCFTypeDictionaryKeyCallBacks.
    /// </summary>
    /// <param name="name">The symbol.</param>
    /// <returns>Its address.</returns>
    public static IntPtr Export(string name)
        => NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), name);
}
