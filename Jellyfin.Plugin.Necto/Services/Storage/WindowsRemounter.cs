using System;
using System.Runtime.InteropServices;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Connects a share again on Windows from C#, the way File Explorer's Map network drive does: WNetGetConnection
/// records the share behind a drive letter, which Windows keeps even while it is down, and WNetAddConnection2 connects it again with the
/// credentials Windows saved. A share used by its \\server\share path is connected the same way. Local disks are
/// left to Windows, which mounts them itself.
/// </summary>
public sealed class WindowsRemounter : IRemounter
{
    private const int ResourceTypeDisk = 1;

    // ERROR_CONNECTION_UNAVAIL: the drive letter is remembered but not connected, and its share is still given.
    private const int ConnectionUnavailable = 1201;

    private const int DeviceAlreadyRemembered = 1202;

    // ERROR_ACCESS_DENIED, ERROR_LOGON_FAILURE, ERROR_BAD_USERNAME, ERROR_INVALID_PASSWORD, ERROR_SESSION_CREDENTIAL_CONFLICT.
    private static readonly int[] SignInErrors = { 5, 1326, 2202, 86, 1219 };

    // ERROR_BAD_NETPATH, ERROR_BAD_NET_NAME, ERROR_NO_NETWORK, ERROR_NETWORK_UNREACHABLE, ERROR_HOST_UNREACHABLE.
    private static readonly int[] UnreachableErrors = { 53, 67, 1222, 1231, 1232 };

    private readonly ILogger<WindowsRemounter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsRemounter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WindowsRemounter(ILogger<WindowsRemounter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TakesAddress => true;

    /// <inheritdoc />
    public string? SourceOf(string mountPoint) => OperatingSystem.IsWindows() ? AddressOf(mountPoint) : null;

    /// <summary>
    /// Finds the share behind a mount point, even while it is disconnected: a \\server\share path is its own share,
    /// and Windows keeps the share of a remembered drive letter while it is down.
    /// </summary>
    /// <param name="mountPoint">A drive letter root or a network path.</param>
    /// <returns>The share, such as \\nas\media, or <c>null</c> for a local disk.</returns>
    private static string? AddressOf(string mountPoint)
    {

        if (mountPoint.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return MountPaths.ShareRoot(mountPoint);
        }

        if (mountPoint.Length < 2 || mountPoint[1] != ':')
        {
            return null;
        }

        var remote = new char[1024];
        var length = remote.Length;
        var status = WNetGetConnectionW(mountPoint[..2], remote, ref length);
        if (status != 0 && status != ConnectionUnavailable)
        {
            return null;
        }

        var end = Array.IndexOf(remote, '\0');
        return new string(remote, 0, end < 0 ? remote.Length : end);
    }

    /// <inheritdoc />
    public RemountResult Remount(string mountPoint, string? source)
    {
        if (!OperatingSystem.IsWindows())
        {
            return RemountResult.Unsupported;
        }

        source ??= AddressOf(mountPoint);
        if (string.IsNullOrEmpty(source))
        {
            return RemountResult.Unsupported;
        }

        var letter = mountPoint.Length >= 2 && mountPoint[1] == ':' ? mountPoint[..2] : null;

        // A drive letter Windows remembers is restored the way File Explorer does when it is opened.
        if (letter is not null && WNetRestoreSingleConnectionW(IntPtr.Zero, letter, false) == 0)
        {
            return RemountResult.Success;
        }

        var resource = new NetResource { Type = ResourceTypeDisk, LocalName = letter, RemoteName = source };
        var status = WNetAddConnection2W(ref resource, null, null, 0);

        // ERROR_DEVICE_ALREADY_REMEMBERED: the letter is remembered but did not restore, so the mapping is made again.
        if (status == DeviceAlreadyRemembered && letter is not null)
        {
            _ = WNetCancelConnection2W(letter, 0, false);
            status = WNetAddConnection2W(ref resource, null, null, 0);
        }

        // ERROR_ALREADY_ASSIGNED: the letter is in use, which the wait for the share to show up then settles.
        if (status is 0 or 85)
        {
            return RemountResult.Success;
        }

        _logger.LogDebug("Windows could not connect {Source}, it returned {Status}", source, status);
        var failure = Array.IndexOf(SignInErrors, status) >= 0 ? RemountFailure.SignIn
            : Array.IndexOf(UnreachableErrors, status) >= 0 ? RemountFailure.Unreachable
            : RemountFailure.Other;
        return RemountResult.Failed(failure, status);
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetGetConnectionW(string localName, [Out] char[] remoteName, ref int length);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetRestoreSingleConnectionW(IntPtr window, string localName, [MarshalAs(UnmanagedType.Bool)] bool useUi);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetCancelConnection2W(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WNetAddConnection2W(ref NetResource resource, string? password, string? userName, int flags);

    // NETRESOURCEW.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
