namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What a library folder holds right now.
/// </summary>
public enum FolderStatus
{
    /// <summary>The folder does not exist.</summary>
    Missing = 0,

    /// <summary>The folder exists and is empty, so Jellyfin skips it in a scan.</summary>
    Empty = 1,

    /// <summary>The folder exists and holds files or folders.</summary>
    Present = 2
}
