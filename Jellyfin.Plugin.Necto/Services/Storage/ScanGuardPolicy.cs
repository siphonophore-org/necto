using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Decides whether a library scan may run. Pure, so the rules are testable.
/// </summary>
public static class ScanGuardPolicy
{
    /// <summary>
    /// Lists the disks that block a scan.
    /// </summary>
    /// <param name="mode">When scans are blocked.</param>
    /// <param name="disks">The disks the last check saw.</param>
    /// <returns>The disks that block it, or none when the scan may run.</returns>
    public static IReadOnlyList<DiskInfo> Blockers(ScanGuardMode mode, IEnumerable<DiskInfo> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);
        return mode switch
        {
            ScanGuardMode.Off => Array.Empty<DiskInfo>(),
            ScanGuardMode.All => disks.Where(d => d.State != DiskState.Available).ToList(),

            // Jellyfin itself skips a library folder that is gone or empty, so only a disk it would scan blocks.
            _ => disks.Where(d => d.State != DiskState.Available && !d.ScanSafe).ToList()
        };
    }
}
