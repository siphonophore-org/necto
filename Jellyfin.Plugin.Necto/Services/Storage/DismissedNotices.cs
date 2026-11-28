using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Reads and writes the dismissed disk notices the Storage settings keep, each as its disk's mount point, a bar, and the
/// notice's key.
/// </summary>
public static class DismissedNotices
{
    /// <summary>
    /// Builds the entry kept for one dismissed notice.
    /// </summary>
    /// <param name="mountPoint">The disk's mount point.</param>
    /// <param name="key">The notice's key.</param>
    /// <returns>The entry.</returns>
    public static string Entry(string mountPoint, string key) => mountPoint + "|" + key;

    /// <summary>
    /// Lists the keys of one disk's dismissed notices.
    /// </summary>
    /// <param name="entries">Every kept entry.</param>
    /// <param name="mountPoint">The disk's mount point.</param>
    /// <returns>The keys, without the mount point.</returns>
    public static List<string> For(IEnumerable<string> entries, string mountPoint)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var prefix = (mountPoint ?? string.Empty) + "|";
        return entries.Where(e => e.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e[prefix.Length..]).ToList();
    }
}
