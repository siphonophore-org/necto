using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Sorts library folders onto the disks they sit on and decides which disks are missing. Pure, so the rules are
/// testable without disks.
/// </summary>
public static class DiskGrouper
{
    /// <summary>
    /// Groups the folders by disk, and records the mount point each reachable folder sits on.
    /// </summary>
    /// <param name="folders">Every library folder.</param>
    /// <param name="mountPoints">The normalized mount points the OS lists.</param>
    /// <param name="status">What each normalized folder holds, or <c>null</c> when it did not answer in time.</param>
    /// <param name="state">The remembered mount points, updated for folders that exist and pruned of folders no library has.</param>
    /// <param name="comparison">How paths compare.</param>
    /// <returns>One group per disk.</returns>
    public static IReadOnlyList<DiskGroup> Group(
        IEnumerable<LibraryFolder> folders,
        IReadOnlyCollection<string> mountPoints,
        Func<string, FolderStatus?> status,
        StorageState state,
        StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(mountPoints);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(state);

        var comparer = StringComparer.FromComparison(comparison);
        var mounted = new HashSet<string>(mountPoints, comparer);
        var known = new Dictionary<string, string>(state.FolderMounts, comparer);
        var groups = new Dictionary<string, DiskGroup>(comparer);
        var seen = new HashSet<string>(comparer);

        foreach (var folder in folders)
        {
            var path = MountPaths.Normalize(folder.Path);
            if (path.Length == 0)
            {
                continue;
            }

            seen.Add(path);
            var current = MountPaths.FindMount(path, mountPoints, comparison);
            known.TryGetValue(path, out var remembered);
            var found = status(path);

            string key;
            DiskState folderState;
            var folderMissing = false;
            if (found is null)
            {
                key = remembered ?? current ?? path;
                folderState = DiskState.Unresponsive;
            }
            else if (remembered is not null && !mounted.Contains(remembered) && !MountPaths.IsShare(remembered))
            {
                // The folder's disk left its mount point, even if leftover files make the folder look present.
                key = remembered;
                folderState = DiskState.Missing;
            }
            else if (found == FolderStatus.Missing)
            {
                var guess = remembered ?? MountPaths.GuessMountPoint(path);
                // A share is never in the mount list, so a gone folder on one means the share is gone.
                if (guess is not null && (MountPaths.IsShare(guess) || !mounted.Contains(guess)))
                {
                    key = guess;
                    folderState = DiskState.Missing;
                }
                else
                {
                    key = current ?? path;
                    folderState = DiskState.Available;
                    folderMissing = true;
                }
            }
            else
            {
                key = current ?? path;
                folderState = DiskState.Available;
                known[path] = key;
            }

            if (!groups.TryGetValue(key, out var group))
            {
                group = new DiskGroup(key) { State = folderState };
                groups[key] = group;
            }
            else if (folderState > group.State)
            {
                group.State = folderState;
            }

            group.Folders.Add(new DiskFolder(path, folder.LibraryName, folder.Server));

            // One of Jellyfin's own folders is made when Jellyfin first needs it, such as transcodes, so one not made
            // yet is not missing. Nor is it scanned, so it never blocks a library scan, and it belongs to no library.
            if (folder.Server)
            {
                continue;
            }

            if (folderMissing)
            {
                group.MissingFolders.Add(path);
            }

            // Jellyfin skips a library folder that is gone or empty, but scans one that still holds files.
            if (folderState == DiskState.Unresponsive || (folderState == DiskState.Missing && found == FolderStatus.Present))
            {
                group.Unguarded = true;
            }

            if (!group.Libraries.Any(l => l.Id == folder.LibraryId))
            {
                group.Libraries.Add(new DiskLibrary(folder.LibraryId, folder.LibraryName));
            }
        }

        state.FolderMounts = known.Where(p => seen.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        return groups.Values.ToList();
    }
}
