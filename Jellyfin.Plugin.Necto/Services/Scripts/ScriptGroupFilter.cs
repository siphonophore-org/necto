using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Scripts;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Decides which users a script runs for. A script runs for every user unless it names groups, and then only for their
/// members, with Everyone standing for the users in no group. Administrators run every script.
/// </summary>
public static class ScriptGroupFilter
{
    /// <summary>
    /// Works out whether a script runs for a user in the given groups.
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="groupIds">The user's groups, Everyone for a user in no group.</param>
    /// <returns><c>true</c> when the script runs for the user.</returns>
    public static bool Runs(ScriptDefinition script, IReadOnlyCollection<Guid> groupIds)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(groupIds);
        return script.AllGroups != false || (script.GroupIds ?? new List<Guid>()).Any(groupIds.Contains);
    }

    /// <summary>
    /// Moves the scripts groups ran onto the scripts. A script every group ran, or no group ran, runs for every user,
    /// and any other takes the groups that ran it. Once every script has its groups, the groups' own picks are cleared.
    /// Ids of groups that no longer exist are dropped.
    /// </summary>
    /// <param name="config">The configuration, changed in place.</param>
    /// <param name="stored">The configuration in effect, whose groups a script sent without them keeps. [optional]</param>
    /// <returns><c>true</c> when anything changed.</returns>
    public static bool Migrate(PluginConfiguration config, PluginConfiguration? stored = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var scripts = config.Scripts?.Scripts ?? new List<ScriptDefinition>();
        var storedScripts = stored?.Scripts?.Scripts ?? new List<ScriptDefinition>();
        var groups = (config.Groups ?? new List<Group>()).Where(g => g is not null).Append(config.Everyone ?? PluginConfiguration.NewEveryone()).ToList();
        var known = groups.Select(g => g.Id).ToHashSet();
        var changed = false;

        foreach (var script in scripts.Where(s => s is not null))
        {
            // A page that leaves the groups out of a script it sends keeps the ones it has.
            if (script.AllGroups is null && storedScripts.Find(s => s?.Id == script.Id) is { AllGroups: not null } kept)
            {
                script.AllGroups = kept.AllGroups;
                script.GroupIds = (kept.GroupIds ?? new List<Guid>()).ToList();
                changed = true;
            }

            if (script.AllGroups is null)
            {
                var running = groups.Where(g => RanBefore(g.Permissions?.Scripts, script.Id)).Select(g => g.Id).ToList();
                script.AllGroups = running.Count == 0 || running.Count == groups.Count;
                script.GroupIds = script.AllGroups == true ? new List<Guid>() : running;
                changed = true;
            }

            var existing = (script.GroupIds ?? new List<Guid>()).Where(known.Contains).Distinct().ToList();
            if (script.GroupIds is null || existing.Count != script.GroupIds.Count)
            {
                script.GroupIds = existing;
                changed = true;
            }
        }

        foreach (var group in groups)
        {
            var picks = group.Permissions?.Scripts;
            if (picks is not null && (picks.AllScripts || picks.ScriptIds.Count > 0))
            {
                picks.AllScripts = false;
                picks.ScriptIds = new List<string>();
                changed = true;
            }
        }

        return changed;
    }

    private static bool RanBefore(ScriptPermissions? picks, Guid scriptId)
        => picks is not null && (picks.AllScripts || (picks.ScriptIds ?? new List<string>()).Exists(id => Guid.TryParse(id, out var g) && g == scriptId));
}
