namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// One library folder, or one of Jellyfin's own folders, on a disk.
/// </summary>
/// <param name="Path">The folder.</param>
/// <param name="Library">The name of the library it belongs to, or for one of Jellyfin's own folders, its string key.</param>
/// <param name="Server">Whether it is one of Jellyfin's own folders, such as its cache.</param>
public sealed record DiskFolder(string Path, string Library, bool Server = false);
