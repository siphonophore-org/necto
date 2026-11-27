using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Finds the group that applies to a user. A user is in one group at most, and Everyone holds a user in none.
/// </summary>
public static class GroupMembership
{
    /// <summary>
    /// Lists the group that applies to a user, as a list of one.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The user's group, the first in list order should a stored one list them twice, or Everyone.</returns>
    public static IReadOnlyList<Group> GroupsOf(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);
        var groups = (config.Groups ?? new List<Group>()).Where(g => g?.MemberIds?.Contains(userId) == true).Take(1).ToList();

        // Everyone only stands in for a user no group holds.
        if (groups.Count == 0)
        {
            groups.Add(config.Everyone ?? PluginConfiguration.NewEveryone());
        }

        return groups;
    }

    /// <summary>
    /// Lists the ids of the groups a user is in, or Everyone's id when the user is in none.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The group ids.</returns>
    public static List<Guid> GroupIdsOf(PluginConfiguration config, Guid userId)
        => GroupsOf(config, userId).Select(g => g.Id).ToList();
}
