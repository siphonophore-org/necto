using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The CoreFoundation and DiskArbitration calls macOS offers for reading a disk's description without root. Only
/// <see cref="MacDiskInventory"/> uses them, and only on macOS.
/// </summary>
internal static class MacNativeMethods
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string DiskArbitration = "/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration";
    private const uint Utf8 = 0x08000100;
    private const nint SInt64Type = 4;

    private static readonly Lazy<IntPtr> _diskArbitration = new(() => NativeLibrary.Load(DiskArbitration));

    /// <summary>
    /// Reads one constant key DiskArbitration exports, such as kDADiskDescriptionMediaSizeKey.
    /// </summary>
    /// <param name="name">The exported name.</param>
    /// <returns>The key, a CFString owned by the framework.</returns>
    public static IntPtr Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(_diskArbitration.Value, name));

    /// <summary>
    /// Reads a string value from a CFDictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The string, or <c>null</c> when the value is missing or not a string.</returns>
    public static string? GetString(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID())
        {
            return null;
        }

        var buffer = new byte[1024];
        return CFStringGetCString(value, buffer, buffer.Length, Utf8)
            ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0) is var end and >= 0 ? end : buffer.Length)
            : null;
    }

    /// <summary>
    /// Reads a whole number from a CFDictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number, or 0 when the value is missing or not a number.</returns>
    public static long GetLong(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        return value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() && CFNumberGetValue(value, SInt64Type, out var number) ? number : 0;
    }

    /// <summary>
    /// Reads a true or false value from a CFDictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <c>false</c> when it is missing or not a boolean.</returns>
    public static bool GetBool(IntPtr dictionary, IntPtr key)
    {
        var value = CFDictionaryGetValue(dictionary, key);
        return value != IntPtr.Zero && CFGetTypeID(value) == CFBooleanGetTypeID() && CFBooleanGetValue(value);
    }

    [DllImport(DiskArbitration)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr DASessionCreate(IntPtr allocator);

    [DllImport(DiskArbitration)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr DADiskCreateFromBSDName(IntPtr allocator, IntPtr session, byte[] name);

    [DllImport(DiskArbitration)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern IntPtr DADiskCopyDescription(IntPtr disk);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nuint CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nuint CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nuint CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nuint CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(IntPtr value, nint type, out long number);

    [DllImport(CoreFoundation)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFBooleanGetValue(IntPtr value);
}
