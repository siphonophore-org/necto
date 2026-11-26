using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Formats a whole unformatted disk with one partition and mounts it where it stays mounted, the way its OS does.
/// Calls block, so callers run them through <see cref="VolumeProbe"/>.
/// </summary>
public interface IDiskFormatter
{
    /// <summary>Gets the file systems this OS can format a disk as, the first being the usual one.</summary>
    IReadOnlyList<string> FileSystems { get; }

    /// <summary>Gets the folder a new disk is mounted in, or <c>null</c> when the OS gives it a drive letter.</summary>
    string? MountRoot { get; }

    /// <summary>Gets a value indicating whether a new disk's folder has dashes for spaces, as on Linux, where macOS keeps them.</summary>
    bool DashesForSpaces { get; }

    /// <summary>
    /// Erases a disk, gives it one partition with a file system, and mounts it.
    /// </summary>
    /// <param name="disk">The disk. The caller has checked it is still unformatted.</param>
    /// <param name="name">The volume name, checked by <see cref="FormatNames"/>.</param>
    /// <param name="fileSystem">One of <see cref="FileSystems"/>.</param>
    /// <returns>What came of it.</returns>
    FormatResult Format(RawDisk disk, string name, string fileSystem);
}
