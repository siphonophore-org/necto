using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The libc calls the plugin uses on Linux and macOS: who Jellyfin runs as, and whether it may write to a folder.
/// </summary>
internal static class UnixNative
{
    /// <summary>W_OK for access: the caller may write.</summary>
    public const int WriteOk = 2;

    [DllImport("libc", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
#pragma warning disable SA1300, CA1707, IDE1006 // The libc names.
    public static extern uint getuid();

    [DllImport("libc", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern uint getgid();

    [DllImport("libc", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);
#pragma warning restore SA1300, CA1707, IDE1006
}
