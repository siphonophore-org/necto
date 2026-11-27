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
    /// Finds the group that applies to a user.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The user's group, the first in list order should a stored one list them twice, or Everyone.</returns>
    public static Group GroupOf(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Everyone only stands in for a user no group holds.
        return (config.Groups ?? new List<Group>()).FirstOrDefault(g => g?.MemberIds?.Contains(userId) == true)
            ?? config.Everyone
            ?? PluginConfiguration.NewEveryone();
    }

    /// <summary>
    /// Lists the id of the group a user is in, or Everyone's id when the user is in none.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The group id, as a list of one.</returns>
    public static List<Guid> GroupIdsOf(PluginConfiguration config, Guid userId)
        => new() { GroupOf(config, userId).Id };
}
