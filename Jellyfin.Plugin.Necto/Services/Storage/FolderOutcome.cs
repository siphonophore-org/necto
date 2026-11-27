namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What the dashboard is told after making a folder.
/// </summary>
/// <param name="Created">Whether the folder was made.</param>
/// <param name="Path">The folder's full path.</param>
/// <param name="ErrorKey">The string key of why it was refused, such as ErrorFolderName, when the plugin refused it.</param>
/// <param name="Error">What the OS said, when the OS failed it.</param>
public sealed record FolderOutcome(bool Created, string? Path = null, string? ErrorKey = null, string? Error = null);
