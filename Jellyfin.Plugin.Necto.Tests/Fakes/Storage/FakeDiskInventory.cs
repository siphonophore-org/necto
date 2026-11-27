using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

/// <summary>
/// Stands in for the OS's list of disks, with the unmounted disks, external mounts, and fstab entries a test sets.
/// </summary>
public sealed class FakeDiskInventory : IDiskInventory
{
    /// <summary>Gets or sets the disks with no mounted file system.</summary>
    public IReadOnlyList<RawDisk> Unmounted { get; set; } = Array.Empty<RawDisk>();

    /// <summary>Gets the mount points on an external disk.</summary>
    public HashSet<string> External { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the mount points fstab names.</summary>
    public HashSet<string> Fstab { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnformatted() => Unmounted;

    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnmounted() => Unmounted;

    /// <inheritdoc />
    public bool IsExternal(string mountPoint) => External.Contains(mountPoint);

    /// <inheritdoc />
    public bool HasFstabEntry(string mountPoint) => Fstab.Contains(mountPoint);
}
