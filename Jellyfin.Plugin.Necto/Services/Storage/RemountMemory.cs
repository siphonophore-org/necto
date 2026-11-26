using System;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What the plugin remembers to mount one disk again: what was mounted there, and how its tries have gone.
/// </summary>
public sealed class RemountMemory
{
    /// <summary>Gets or sets what was mounted at the mount point when it was last seen, such as smb://nas/media, or <c>null</c> when the OS needs none.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets the address the failed tries used, so a new address set by hand is tried at once.</summary>
    public string? TriedSource { get; set; }

    /// <summary>Gets or sets the failed tries in a row since the disk went missing.</summary>
    public int Failures { get; set; }

    /// <summary>Gets or sets when the next try may run, in UTC, or <c>null</c> when it may run at the next check.</summary>
    public DateTime? RetryAfterUtc { get; set; }
}
