using System;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// One change in one disk between two checks.
/// </summary>
/// <param name="Kind">What changed.</param>
/// <param name="Disk">The disk as this check saw it.</param>
/// <param name="Away">How long the disk was missing or unresponsive, for <see cref="DiskEventKind.Returned"/>.</param>
public sealed record DiskEvent(DiskEventKind Kind, DiskInfo Disk, TimeSpan? Away = null);
