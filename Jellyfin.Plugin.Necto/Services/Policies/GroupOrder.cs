using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Puts the groups in the order an administrator chose. The order decides whose template a member of several groups
/// takes, and Everyone, which is kept apart, always comes first.
/// </summary>
public static class GroupOrder
{
    /// <summary>
    /// Reorders the groups in place.
    /// </summary>
    /// <param name="groups">The groups, without Everyone.</param>
    /// <param name="ids">Every group's id once, in the new order.</param>
    /// <returns><c>true</c> when the ids named every group once and the groups were reordered.</returns>
    public static bool Apply(List<Group> groups, IReadOnlyList<Guid>? ids)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (ids is null || ids.Count != groups.Count || ids.Distinct().Count() != ids.Count || !ids.All(id => groups.Any(g => g.Id == id)))
        {
            return false;
        }

        var ordered = ids.Select(id => groups.First(g => g.Id == id)).ToList();
        groups.Clear();
        groups.AddRange(ordered);
        return true;
    }
}
