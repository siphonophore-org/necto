namespace Jellyfin.Plugin.Necto.Configuration.Storage;

/// <summary>
/// When a library scan is blocked while a disk is missing.
/// </summary>
public enum ScanGuardMode
{
    /// <summary>Block a scan only when a missing disk's folders would not be skipped by Jellyfin itself.</summary>
    Affected = 0,

    /// <summary>Block every scan while any disk is missing or unresponsive.</summary>
    All = 1,

    /// <summary>Never block a scan.</summary>
    Off = 2
}
