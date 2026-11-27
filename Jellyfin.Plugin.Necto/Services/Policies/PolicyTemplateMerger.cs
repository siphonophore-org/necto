using System;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using Jellyfin.Plugin.Necto.Services.Groups;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Works out the Jellyfin settings a user takes from their one group. A row the group does not set is left to the
/// user, and a user in no group takes Everyone's.
/// </summary>
public static class PolicyTemplateMerger
{
    /// <summary>
    /// Builds the template a user takes from their group.
    /// </summary>
    /// <param name="config">The configuration that holds the groups.</param>
    /// <param name="userId">The user.</param>
    /// <returns>A new template, which sets no row when the user's group sets none.</returns>
    public static PolicyTemplate For(PluginConfiguration config, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(config);

        var copy = SecretJson.Copy(GroupMembership.GroupOf(config, userId).Template ?? new PolicyTemplate());
        PolicyTemplateNormalizer.Complete(copy);
        return PolicyTemplateRows.CopySetRows(copy);
    }
}
