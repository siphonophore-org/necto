namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What a mounted volume reports about itself.
/// </summary>
/// <param name="TotalBytes">The volume's size.</param>
/// <param name="FreeBytes">The space the server's account may still use.</param>
/// <param name="FileSystem">The file system, such as ext4, apfs, or nfs.</param>
/// <param name="IsNetwork"><c>true</c> when the volume is a network share.</param>
/// <param name="Label">The volume's own name, when it has one apart from its mount point.</param>
public sealed record VolumeReading(long TotalBytes, long FreeBytes, string? FileSystem, bool IsNetwork, string? Label);
