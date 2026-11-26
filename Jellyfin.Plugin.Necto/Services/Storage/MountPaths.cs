using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Path rules for finding the mount point a folder sits on. Pure, so every rule is testable on any OS.
/// </summary>
public static class MountPaths
{
    // Volumes smaller than this are boot and firmware partitions, never media disks.
    private const long MinimumVolumeBytes = 1024L * 1024 * 1024;

    private static readonly HashSet<string> SystemFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "autofs", "binfmt_misc", "bpf", "cgroup", "cgroup2", "configfs", "debugfs", "devfs", "devpts", "devtmpfs", "efivarfs",
        "devicefs", "fuse.gvfsd-fuse", "fuse.lxcfs", "fuse.portal", "fusectl", "hugetlbfs", "mqueue", "none", "nsfs", "nullfs", "overlay",
        "proc", "pstore", "ramfs", "rpc_pipefs", "securityfs", "squashfs", "sysfs", "tmpfs", "tracefs"
    };

    private static readonly string[] SystemFolders =
    {
        "/proc", "/sys", "/dev", "/run", "/snap", "/boot", "/var/lib/docker", "/var/lib/containers",
        "/System/Volumes", "/private", "/Library/Developer"
    };

    // macOS mounts its own helper volumes under /Volumes, beside the disks a person attaches.
    private static readonly HashSet<string> MacSystemVolumes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Recovery", "Preboot", "VM", "Update", "xarts", "iSCPreboot", "Hardware"
    };

    /// <summary>
    /// Gets how paths compare on this OS. Windows and macOS file systems ignore case by default.
    /// </summary>
    public static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Removes a trailing separator, except from a root such as / or C:\, so two spellings of a folder match.
    /// </summary>
    /// <param name="path">A folder path.</param>
    /// <returns>The path with no trailing separator.</returns>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.Trim();
        while (trimmed.Length > 1 && IsSeparator(trimmed[^1]) && !IsRoot(trimmed))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed;
    }

    /// <summary>
    /// Tells whether a path is a mount point itself or sits inside it.
    /// </summary>
    /// <param name="path">A normalized folder path.</param>
    /// <param name="mountPoint">A normalized mount point.</param>
    /// <param name="comparison">How the two compare.</param>
    /// <returns><c>true</c> when the path is on the mount point.</returns>
    public static bool IsUnder(string path, string mountPoint, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mountPoint);
        if (!path.StartsWith(mountPoint, comparison))
        {
            return false;
        }

        // A root mount point already ends in a separator, so anything that starts with it is inside.
        return path.Length == mountPoint.Length
            || IsSeparator(mountPoint[^1])
            || IsSeparator(path[mountPoint.Length]);
    }

    /// <summary>
    /// Finds the deepest mount point a path sits on.
    /// </summary>
    /// <param name="path">A normalized folder path.</param>
    /// <param name="mountPoints">The normalized mount points the OS lists.</param>
    /// <param name="comparison">How paths compare.</param>
    /// <returns>The mount point, or <c>null</c> when none holds the path.</returns>
    public static string? FindMount(string path, IEnumerable<string> mountPoints, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(mountPoints);
        if (IsShare(path))
        {
            return ShareRoot(path);
        }

        string? best = null;
        foreach (var mount in mountPoints)
        {
            if (IsUnder(path, mount, comparison) && (best is null || mount.Length > best.Length))
            {
                best = mount;
            }
        }

        return best;
    }

    /// <summary>
    /// Tells whether a path is a Windows network path, such as \\nas\media.
    /// </summary>
    /// <param name="path">A folder path.</param>
    /// <returns><c>true</c> for a network path.</returns>
    public static bool IsShare(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the share a Windows network path is on, such as \\nas\media for \\nas\media\Movies.
    /// </summary>
    /// <param name="path">A network path.</param>
    /// <returns>The server and share, or the path itself when it names no share.</returns>
    public static string ShareRoot(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var separator = path[0];
        var parts = path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? path : string.Concat(separator, separator, parts[0], separator, parts[1]);
    }

    /// <summary>
    /// Guesses where a folder's disk mounts when the folder is gone and was never seen, from the usual places disks
    /// mount, such as /Volumes/Media, /mnt/media, or D:\.
    /// </summary>
    /// <param name="path">A normalized folder path.</param>
    /// <returns>The likely mount point, or <c>null</c> when the path is in none of the usual places.</returns>
    public static string? GuessMountPoint(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (IsShare(path))
        {
            return ShareRoot(path);
        }

        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
        {
            return path[..3];
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && (parts[0] is "Volumes" or "mnt" or "media"))
        {
            return "/" + parts[0] + "/" + parts[1];
        }

        if (parts.Length >= 4 && parts[0] == "run" && parts[1] == "media")
        {
            return "/run/media/" + parts[2] + "/" + parts[3];
        }

        return null;
    }

    /// <summary>
    /// Gets the name shown for a disk: its own label when it has one, else the last part of its mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="label">The volume's own label, if any.</param>
    /// <returns>The name.</returns>
    public static string DisplayName(string mountPoint, string? label)
    {
        ArgumentNullException.ThrowIfNull(mountPoint);
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        if (IsRoot(mountPoint))
        {
            return mountPoint;
        }

        var name = Path.GetFileName(Normalize(mountPoint).Replace('\\', '/'));
        return string.IsNullOrEmpty(name) ? mountPoint : name;
    }

    /// <summary>
    /// Gets the mount point of the disk the OS runs from: the drive holding the Windows folder, such as C:\, or /.
    /// </summary>
    public static string SystemMountPoint
        => OperatingSystem.IsWindows() ? Normalize(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\") : "/";

    /// <summary>
    /// Tells whether a mounted volume is a disk a person would add media to, rather than one the OS keeps for itself,
    /// such as tmpfs, /proc, a container layer, or macOS's system volumes.
    /// </summary>
    /// <param name="mountPoint">The normalized mount point.</param>
    /// <param name="fileSystem">The file system, such as ext4 or tmpfs.</param>
    /// <param name="totalBytes">The volume's size.</param>
    /// <returns><c>true</c> for a disk worth listing.</returns>
    public static bool IsUserVolume(string mountPoint, string? fileSystem, long totalBytes)
    {
        ArgumentNullException.ThrowIfNull(mountPoint);
        if (totalBytes < MinimumVolumeBytes || (fileSystem is not null && SystemFileSystems.Contains(fileSystem)))
        {
            return false;
        }

        if (mountPoint.StartsWith("/run/media/", StringComparison.Ordinal))
        {
            return true;
        }

        if (mountPoint.Contains("/Library/Developer/", StringComparison.Ordinal)
            || (mountPoint.StartsWith("/Volumes/", StringComparison.Ordinal) && MacSystemVolumes.Contains(mountPoint["/Volumes/".Length..])))
        {
            return false;
        }

        return !SystemFolders.Any(folder => IsUnder(mountPoint, folder, StringComparison.Ordinal));
    }

    private static bool IsSeparator(char c) => c is '/' or '\\';

    private static bool IsRoot(string path)
        => path == "/" || path == "\\" || (path.Length == 3 && path[1] == ':' && IsSeparator(path[2]));
}
