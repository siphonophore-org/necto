namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What macOS's DiskArbitration describes of one disk device.
/// </summary>
/// <param name="Device">The disk's BSD name, such as disk4.</param>
/// <param name="Whole">Whether the device is a whole disk rather than a partition.</param>
/// <param name="Leaf">Whether the device has no partitions or volumes under it.</param>
/// <param name="Content">The partition scheme or content type, empty when the disk holds nothing.</param>
/// <param name="VolumeKind">The file system of a volume on the device itself, such as apfs, or <c>null</c>.</param>
/// <param name="Name">The media name, such as Samsung SSD 870 EVO Media.</param>
/// <param name="Model">The device model, such as Samsung SSD 870 EVO.</param>
/// <param name="SizeBytes">The media size.</param>
public sealed record MacDiskDescription(string Device, bool Whole, bool Leaf, string? Content, string? VolumeKind, string? Name, string? Model, long SizeBytes, bool External = false)
{
    /// <summary>
    /// Gets a value indicating whether the device is a whole disk that holds nothing: no partition scheme, no
    /// partitions or volumes under it, and no file system of its own.
    /// </summary>
    public bool IsBlank => Whole && Leaf && string.IsNullOrEmpty(Content) && string.IsNullOrEmpty(VolumeKind) && SizeBytes > 0;
}
