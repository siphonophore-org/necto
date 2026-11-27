using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The Windows calls for reading a physical disk's size, model, and partition layout. Only
/// <see cref="WindowsDiskInventory"/> uses them, and only on Windows.
/// </summary>
internal static class WindowsNativeMethods
{
    /// <summary>Reads a disk's partition layout. Needs read access, so the caller must be an administrator.</summary>
    public const uint IoctlDiskGetDriveLayoutEx = 0x00070050;

    /// <summary>Reads a disk's size in bytes. Needs read access.</summary>
    public const uint IoctlDiskGetLengthInfo = 0x0007405C;

    /// <summary>Reads a device's storage descriptor, which names its vendor and product.</summary>
    public const uint IoctlStorageQueryProperty = 0x002D1400;

    /// <summary>Opens a device for reading.</summary>
    public const uint GenericRead = 0x80000000;

    /// <summary>Lets other programs keep reading and writing the device while it is open.</summary>
    public const uint ShareReadWrite = 0x00000003;

    /// <summary>Opens only a device that already exists.</summary>
    public const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
