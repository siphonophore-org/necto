using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Mounts a missing disk again, the way its OS does. Calls may block, so callers run them through
/// <see cref="VolumeProbe"/>.
/// </summary>
public interface IRemounter
{
    /// <summary>
    /// Gets a value indicating whether a share's address can be set by hand, for a share that was already gone when
    /// the plugin first looked. On Linux the /etc/fstab entry says where a disk comes from, so none is taken.
    /// </summary>
    bool TakesAddress { get; }

    /// <summary>
    /// Reads what is mounted at a mount point while it is up, so it can be mounted again once it is gone.
    /// </summary>
    /// <param name="mountPoint">A mounted mount point.</param>
    /// <returns>The source, such as smb://nas/media, or <c>null</c> when there is none to remember.</returns>
    string? SourceOf(string mountPoint);

    /// <summary>
    /// Mounts a missing disk again at its mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="source">What <see cref="SourceOf"/> gave while it was mounted, or <c>null</c>.</param>
    /// <returns>What came of it.</returns>
    RemountResult Remount(string mountPoint, string? source);
}
