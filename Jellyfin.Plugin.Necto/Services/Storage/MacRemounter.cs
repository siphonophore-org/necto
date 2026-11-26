using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Mounts a share again on macOS from C#, the way Finder's Connect to Server does: statfs records the share's URL
/// while it is mounted, and NetFS mounts that URL again with the password saved in the keychain, without asking.
/// Local disks are left to macOS, which mounts them itself when they are plugged in.
/// </summary>
public sealed class MacRemounter : IRemounter
{
    // NetFS's errors for a share that refused the sign-in, or found none: EAUTH, and NetAuth's denied and no credentials.
    private static readonly int[] SignInErrors = { 80, -6003, -6600, -5045 };

    // ENOENT, EHOSTUNREACH, EHOSTDOWN, ETIMEDOUT, ENETUNREACH, ECONNREFUSED, and NetAuth's host not found.
    private static readonly int[] UnreachableErrors = { 2, 65, 64, 60, 51, 61, -6602 };

    // EPERM and EACCES.
    private static readonly int[] DeniedErrors = { 1, 13 };

    private const string VolumesFolder = "/Volumes";

    private readonly ILogger<MacRemounter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacRemounter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MacRemounter(ILogger<MacRemounter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TakesAddress => true;

    /// <inheritdoc />
    public string? SourceOf(string mountPoint)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var buffer = new byte[MacMountNative.StatfsSize];
        if (MacMountNative.Statfs(Encoding.UTF8.GetBytes(mountPoint + "\0"), buffer) != 0)
        {
            return null;
        }

        // statfs reads the file system the path is on, which is the boot volume once the share is gone.
        var mountedOn = Text(buffer, MacMountNative.MountedOnOffset, 1024);
        if (!string.Equals(MountPaths.Normalize(mountedOn), MountPaths.Normalize(mountPoint), StringComparison.Ordinal))
        {
            return null;
        }

        return ShareUrl(Text(buffer, MacMountNative.TypeNameOffset, 16), Text(buffer, MacMountNative.MountedFromOffset, 1024));
    }

    /// <summary>
    /// Builds the URL a share was mounted from, out of statfs's file system type and source.
    /// </summary>
    /// <param name="fileSystem">The file system type, such as smbfs.</param>
    /// <param name="mountedFrom">The source, such as //jp@nas._smb._tcp.local/Media.</param>
    /// <returns>The URL, such as smb://jp@nas._smb._tcp.local/Media, or <c>null</c> for a local disk.</returns>
    public static string? ShareUrl(string fileSystem, string mountedFrom)
    {
        if (string.IsNullOrEmpty(mountedFrom))
        {
            return null;
        }

        switch (fileSystem)
        {
            case "smbfs" when mountedFrom.StartsWith("//", StringComparison.Ordinal):
                return "smb:" + mountedFrom;
            case "afpfs" when mountedFrom.StartsWith("//", StringComparison.Ordinal):
                return "afp:" + mountedFrom;
            case "nfs":
                // host:/export/path, or [v6 address]:/export/path.
                var colon = mountedFrom.IndexOf(":/", StringComparison.Ordinal);
                return colon > 0 ? "nfs://" + mountedFrom[..colon] + mountedFrom[(colon + 1)..] : null;
            case "webdav" when Uri.TryCreate(mountedFrom, UriKind.Absolute, out _):
                return mountedFrom;
            default:
                return null;
        }
    }

    /// <inheritdoc />
    public RemountResult Remount(string mountPoint, string? source)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrEmpty(source))
        {
            return RemountResult.Unsupported;
        }

        // A share whose folder is still there goes back into it, which macOS leaves in place for one mounted outside
        // /Volumes. A share in /Volumes whose folder is gone goes back under the share's name, as Finder mounts it.
        var parent = Path.GetDirectoryName(MountPaths.Normalize(mountPoint));
        var inVolumes = string.Equals(parent, VolumesFolder, StringComparison.Ordinal);

        // A folder left behind in /Volumes would push a share Finder style to a new name, such as Media-1, so the
        // share goes back into the folder that is there.
        var intoFolder = Directory.Exists(mountPoint);
        if (!inVolumes && !intoFolder)
        {
            _logger.LogDebug("Can't remount {Source} because {MountPoint} is gone", source, mountPoint);
            return RemountResult.Failed(RemountFailure.Other, 2);
        }

        var handles = new CfHandles();
        try
        {
            var text = handles.Add(MacMountNative.CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(source + "\0"), MacMountNative.Utf8));
            var url = handles.Add(MacMountNative.CFURLCreateWithString(IntPtr.Zero, text, IntPtr.Zero));
            if (url == IntPtr.Zero)
            {
                return RemountResult.Failed(RemountFailure.Other);
            }

            var keys = MacMountNative.Export("kCFTypeDictionaryKeyCallBacks");
            var values = MacMountNative.Export("kCFTypeDictionaryValueCallBacks");

            // Never show a sign-in window: a server has no one to answer it, so only the keychain is used.
            var openOptions = handles.Add(MacMountNative.CFDictionaryCreateMutable(IntPtr.Zero, 0, keys, values));
            MacMountNative.CFDictionarySetValue(openOptions, handles.Add(CfString("UIOption")), handles.Add(CfString("NoUI")));

            var mountOptions = handles.Add(MacMountNative.CFDictionaryCreateMutable(IntPtr.Zero, 0, keys, values));
            var mountPath = IntPtr.Zero;
            if (intoFolder)
            {
                var path = Encoding.UTF8.GetBytes(mountPoint);
                mountPath = handles.Add(MacMountNative.CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, true));
                MacMountNative.CFDictionarySetValue(mountOptions, handles.Add(CfString("MountAtMountDir")), Marshal.ReadIntPtr(MacMountNative.Export("kCFBooleanTrue")));
            }

            var status = MacMountNative.NetFSMountURLSync(url, mountPath, IntPtr.Zero, IntPtr.Zero, openOptions, mountOptions, out var mounted);
            handles.Add(mounted);

            // EEXIST: it is already mounted.
            if (status is 0 or 17)
            {
                return RemountResult.Success;
            }

            _logger.LogDebug("NetFS could not mount {Source}, it returned {Status}", source, status);
            return RemountResult.Failed(Classify(status), status);
        }
        finally
        {
            handles.Dispose();
        }
    }

    private static RemountFailure Classify(int status)
        => Array.IndexOf(SignInErrors, status) >= 0 ? RemountFailure.SignIn
            : Array.IndexOf(UnreachableErrors, status) >= 0 ? RemountFailure.Unreachable
            : Array.IndexOf(DeniedErrors, status) >= 0 ? RemountFailure.Denied
            : RemountFailure.Other;

    private static IntPtr CfString(string text)
        => MacMountNative.CFStringCreateWithCString(IntPtr.Zero, Encoding.UTF8.GetBytes(text + "\0"), MacMountNative.Utf8);

    private static string Text(byte[] buffer, int offset, int length)
    {
        var span = buffer.AsSpan(offset, length);
        var end = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
    }

    // The CoreFoundation objects one mount creates, released together.
    private sealed class CfHandles : IDisposable
    {
        private readonly System.Collections.Generic.List<IntPtr> _handles = new();

        public IntPtr Add(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                _handles.Add(handle);
            }

            return handle;
        }

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                MacNativeMethods.CFRelease(handle);
            }

            _handles.Clear();
        }
    }
}
