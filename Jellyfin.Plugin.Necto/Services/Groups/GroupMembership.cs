using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Groups;

/// <summary>
/// Finds the groups that apply to a user: every group the user is in, or Everyone when the user is in none.
/// </summary>
public static class GroupMembership
{
    /// <summary>
    /// Lists the groups that apply to a user.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>The user's groups in list order, or only Everyone when the user is in none.</returns>
    public static IReadOnlyList<Group> GroupsOf(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);
        var groups = (config.Groups ?? new List<Group>()).Where(g => g?.MemberIds?.Contains(userId) == true).ToList();

        // Everyone only stands in for a user no group holds, so it never adds to a real group's permissions.
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
