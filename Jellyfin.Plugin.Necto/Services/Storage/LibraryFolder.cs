namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// One folder of one library, or one of Jellyfin's own folders, such as its cache.
/// </summary>
/// <param name="LibraryId">The library's ID.</param>
/// <param name="LibraryName">The library's name.</param>
/// <param name="Path">The folder, as the library lists it.</param>
/// <param name="Server">Whether it is one of Jellyfin's own folders, whose name is a string key such as LabelCache.</param>
public sealed record LibraryFolder(string LibraryId, string LibraryName, string Path, bool Server = false);
