using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The rules for a new volume's name and the folder it is mounted at. Pure, so they are testable.
/// </summary>
public static class FormatNames
{
    /// <summary>The longest name, which every file system the plugin formats can hold, exFAT's being the shortest.</summary>
    public const int MaxLength = 11;

    /// <summary>
    /// Tells whether a volume name is allowed: 1 to 11 letters, numbers, spaces, dashes, or underscores, starting
    /// with a letter or number.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><c>true</c> when it is allowed.</returns>
    public static bool IsValid(string? name)
        => !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && char.IsAsciiLetterOrDigit(name[0])
            && name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_');

    /// <summary>
    /// Gets the folder a new volume is mounted at in a mount root, such as /mnt/Media-2 on Linux, which has dashes for
    /// spaces, or /Volumes/Media 2 on macOS, which keeps them.
    /// </summary>
    /// <param name="root">The mount root, such as /mnt.</param>
    /// <param name="name">A valid volume name.</param>
    /// <param name="dashesForSpaces">Whether spaces become dashes.</param>
    /// <returns>The mount point.</returns>
    public static string MountPoint(string root, string name, bool dashesForSpaces = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Path.Combine(root, dashesForSpaces ? name.Replace(' ', '-') : name);
    }
}
