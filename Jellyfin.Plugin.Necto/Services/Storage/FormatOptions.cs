using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What formatting offers on this server.
/// </summary>
public sealed class FormatOptions
{
    /// <summary>Gets or sets the file systems a disk can be formatted as, the first being the usual one.</summary>
    public IReadOnlyList<string> FileSystems { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the folder a new disk is mounted in, such as /mnt, or <c>null</c> when it gets a drive letter.</summary>
    public string? MountRoot { get; set; }

    /// <summary>Gets or sets a value indicating whether a new disk's folder has dashes for spaces.</summary>
    public bool DashesForSpaces { get; set; }
}
