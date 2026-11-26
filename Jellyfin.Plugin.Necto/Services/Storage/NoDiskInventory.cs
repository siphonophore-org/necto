using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// The disk inventory on an OS the plugin has no reader for. It finds no unformatted disks.
/// </summary>
public sealed class NoDiskInventory : IDiskInventory
{
    /// <inheritdoc />
    public IReadOnlyList<RawDisk> GetUnformatted() => Array.Empty<RawDisk>();
}
