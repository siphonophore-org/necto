namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// A request to make a folder on a mounted disk and add it to a library.
/// </summary>
public sealed class FolderRequest
{
    /// <summary>Gets or sets the disk's mount point, such as /mnt/cache.</summary>
    public string MountPoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder's name, made directly on the disk, such as Movies.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the library to add the folder to, by name, or <c>null</c> for none.</summary>
    public string? Library { get; set; }
}
