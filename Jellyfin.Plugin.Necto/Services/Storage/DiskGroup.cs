using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The library folders that sit on one mount point, before the volume itself is read.
/// </summary>
/// <param name="MountPoint">The mount point the folders sit on, or were last seen on.</param>
public sealed record DiskGroup(string MountPoint)
{
    /// <summary>Gets or sets whether the disk can be read, from its folders alone.</summary>
    public DiskState State { get; set; }

    /// <summary>Gets or sets a value indicating whether a folder of this missing disk still holds files, or did not answer, so Jellyfin would scan it.</summary>
    public bool Unguarded { get; set; }

    /// <summary>Gets the libraries with a folder on the disk.</summary>
    public List<DiskLibrary> Libraries { get; } = new();

    /// <summary>Gets the library folders on the disk.</summary>
    public List<DiskFolder> Folders { get; } = new();

    /// <summary>Gets the folders that are gone while the disk is mounted.</summary>
    public List<string> MissingFolders { get; } = new();
}
