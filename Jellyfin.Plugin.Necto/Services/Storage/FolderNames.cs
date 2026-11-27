using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The rules for a folder made on a disk: one name, directly on the disk, that every OS can hold. Pure, so they are
/// testable.
/// </summary>
public static class FolderNames
{
    /// <summary>The longest name.</summary>
    public const int MaxLength = 100;

    // Characters Windows forbids in a name, which also keeps out every path separator.
    private static readonly char[] Forbidden = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>
    /// Tells whether a folder name is allowed: 1 to 100 characters, no path separator or character Windows forbids,
    /// no control character, not . or .., and no space at either end.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><c>true</c> when it is allowed.</returns>
    public static bool IsValid(string? name)
        => !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && name != "." && name != ".."
            && name.Trim() == name
            && !name.EndsWith('.')
            && name.IndexOfAny(Forbidden) < 0
            && !name.Any(char.IsControl);

    /// <summary>
    /// Gets the folder's path on a disk, or <c>null</c> when it would not sit directly on that disk.
    /// </summary>
    /// <param name="mountPoint">The disk's mount point.</param>
    /// <param name="name">A valid folder name.</param>
    /// <returns>The path.</returns>
    public static string? PathOn(string mountPoint, string name)
    {
        ArgumentNullException.ThrowIfNull(mountPoint);
        if (!IsValid(name))
        {
            return null;
        }

        var root = Path.GetFullPath(mountPoint);
        var path = Path.GetFullPath(Path.Combine(root, name));
        return string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(root), MountPaths.Comparison) ? path : null;
    }
}
