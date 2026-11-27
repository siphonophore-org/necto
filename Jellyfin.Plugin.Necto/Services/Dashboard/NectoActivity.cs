using System;
using System.Linq;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// Tells Necto's own activity log entries from the rest, by the types its tools write.
/// </summary>
public static class NectoActivity
{
    /// <summary>
    /// The type names Necto's tools write under. Most write under Necto, and Requests, Issues, WireGuard, Storage, and
    /// Scripts under their own names.
    /// </summary>
    public static readonly string[] TypeRoots = { "Necto", "Requests", "WireGuard", "Storage", "Scripts" };

    /// <summary>
    /// Whether an entry type is one of Necto's: a root itself, or a root followed by a dot and more.
    /// </summary>
    /// <param name="type">The entry's type.</param>
    /// <returns><c>true</c> when a Necto tool wrote it.</returns>
    public static bool Owns(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return false;
        }

        return TypeRoots.Any(root => type.StartsWith(root, StringComparison.Ordinal)
            && (type.Length == root.Length || type[root.Length] == '.'));
    }
}
