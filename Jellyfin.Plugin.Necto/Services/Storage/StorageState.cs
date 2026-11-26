using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// What the plugin remembers between checks, kept in its state file.
/// </summary>
public sealed class StorageState
{
    /// <summary>Gets or sets the mount point each library folder was last seen on.</summary>
    public Dictionary<string, string> FolderMounts { get; set; } = new();

    /// <summary>Gets or sets when each missing or unresponsive disk was first seen that way, by mount point, in UTC.</summary>
    public Dictionary<string, DateTime> MissingSince { get; set; } = new();

    /// <summary>Gets or sets what the last check saw of each disk, by mount point.</summary>
    public Dictionary<string, DiskMemory> Disks { get; set; } = new();

    /// <summary>Gets or sets what is needed to mount each disk again, by mount point.</summary>
    public Dictionary<string, RemountMemory> Remounts { get; set; } = new();
}
