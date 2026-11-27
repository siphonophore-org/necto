using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Picks the template a user takes. A template writes whole Jellyfin settings, which cannot be merged field by
/// field, so the user takes the template of the first of their groups, in group order, that has one turned on.
/// A user in no group takes Everyone's.
/// </summary>
public static class PolicyTemplateMerger
{
    /// <summary>
    /// Finds the group whose template a user takes.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The user.</param>
    /// <returns>The group, or <c>null</c> when none of the user's groups has a template on.</returns>
    public static Group? GroupFor(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);

        // GroupsOf keeps the order of the group list, so the first match is the highest group.
        return GroupMembership.GroupsOf(config, userId).FirstOrDefault(g => g.Template is { Enabled: true });
    }
}
