using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Compares a check with what the last one saw, so the activity log records only what changed. Pure, so the
/// rules are testable.
/// </summary>
public static class DiskEvents
{
    /// <summary>
    /// Lists what changed in each disk, and updates the memory to this check.
    /// </summary>
    /// <param name="disks">The disks this check saw, with their levels set.</param>
    /// <param name="memory">What the last check saw, by mount point. Replaced with what this check saw.</param>
    /// <param name="missingSince">When each disk went missing, from before this check, by mount point.</param>
    /// <param name="now">This check's time, in UTC.</param>
    /// <returns>The changes, in the order of the disks.</returns>
    public static IReadOnlyList<DiskEvent> Compare(
        IEnumerable<DiskInfo> disks,
        IDictionary<string, DiskMemory> memory,
        IReadOnlyDictionary<string, DateTime> missingSince,
        DateTime now,
        HealthAlerts? health = null)
    {
        health ??= new HealthAlerts();
        ArgumentNullException.ThrowIfNull(disks);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(missingSince);

        var events = new List<DiskEvent>();
        var next = new Dictionary<string, DiskMemory>(StringComparer.FromComparison(MountPaths.Comparison));
        foreach (var disk in disks)
        {
            // A disk first seen counts as mounted with room, so one already missing is still recorded.
            var before = memory.TryGetValue(disk.MountPoint, out var seen) ? seen : new DiskMemory();
            if (disk.State != before.State)
            {
                if (disk.State == DiskState.Missing)
                {
                    events.Add(new DiskEvent(DiskEventKind.WentMissing, disk));
                }
                else if (disk.State == DiskState.Unresponsive)
                {
                    events.Add(new DiskEvent(DiskEventKind.BecameUnresponsive, disk));
                }
                else
                {
                    TimeSpan? away = missingSince.TryGetValue(disk.MountPoint, out var since) ? now - since : null;
                    events.Add(new DiskEvent(DiskEventKind.Returned, disk, away));
                }
            }

            if (disk.State == DiskState.Available && disk.Level > before.Level)
            {
                events.Add(new DiskEvent(disk.Level == SpaceLevel.Error ? DiskEventKind.Full : DiskEventKind.Low, disk));
            }

            if (health.Failing && disk.State == DiskState.Available && disk.Health == DiskHealth.Failing && before.Health != DiskHealth.Failing)
            {
                events.Add(new DiskEvent(DiskEventKind.Failing, disk));
            }

            if (disk.State == DiskState.Available && disk.Hot && !before.Hot)
            {
                events.Add(new DiskEvent(DiskEventKind.Hot, disk));
            }

            if (disk.State == DiskState.Available && disk.Worn && !before.Worn)
            {
                events.Add(new DiskEvent(DiskEventKind.Worn, disk));
            }

            // A disk that cannot be read keeps the level and health it last had, so reading it again does not record
            // it twice.
            var level = disk.State == DiskState.Available ? disk.Level : before.Level;
            // While the Failing alert is off, a failing disk is not remembered as recorded, so turning it on records it.
            var condition = disk.State == DiskState.Available && disk.Health != DiskHealth.Unknown ? disk.Health : before.Health;
            if (!health.Failing && condition == DiskHealth.Failing)
            {
                condition = DiskHealth.Unknown;
            }
            var read = disk.State == DiskState.Available;
            next[disk.MountPoint] = new DiskMemory
            {
                State = disk.State,
                Level = level,
                Health = condition,
                Kind = read ? disk.Kind : before.Kind,
                Hot = read ? disk.Hot : before.Hot,
                Worn = read ? disk.Worn : before.Worn
            };
        }

        memory.Clear();
        foreach (var pair in next)
        {
            memory[pair.Key] = pair.Value;
        }

        return events;
    }
}
