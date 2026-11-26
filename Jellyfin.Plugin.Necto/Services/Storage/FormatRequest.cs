namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// A request to format an unformatted disk and mount it.
/// </summary>
public sealed class FormatRequest
{
    /// <summary>Gets or sets the disk, as the Storage tab lists it, such as /dev/sdb, disk4, or Disk 2.</summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>Gets or sets the new volume's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the file system, one of the formatter's.</summary>
    public string FileSystem { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a disk that holds data may be erased. A blank disk needs none.</summary>
    public bool Erase { get; set; }

    /// <summary>Gets or sets the library to add the new disk to, by name, or <c>null</c> for none.</summary>
    public string? Library { get; set; }
}
