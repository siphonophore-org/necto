using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using Jellyfin.Plugin.Necto.Services.Groups;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Works out the Jellyfin settings a user takes. Each row comes from the first of the user's groups, in group order,
/// that sets it, and a row no group sets is left to the user. A user in no group takes Everyone's.
/// </summary>
public static class PolicyTemplateMerger
{
    /// <summary>
    /// Builds the template a user takes from their groups.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The user.</param>
    /// <returns>A new template, which sets no row when none of the user's groups sets one.</returns>
    public static PolicyTemplate For(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);

        // GroupsOf keeps the order of the group list, so the first group to set a row is the highest one.
        var templates = GroupMembership.GroupsOf(config, userId).Select(g =>
        {
            var copy = SecretJson.Copy(g.Template ?? new PolicyTemplate());
            PolicyTemplateNormalizer.Complete(copy);
            return copy;
        });
        return PolicyTemplateRows.Merge(templates);
    }
}
