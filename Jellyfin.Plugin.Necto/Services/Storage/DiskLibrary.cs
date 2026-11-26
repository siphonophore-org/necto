namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// A library with at least one folder on a disk.
/// </summary>
/// <param name="Id">The library's ID.</param>
/// <param name="Name">The library's name.</param>
public sealed record DiskLibrary(string Id, string Name);
