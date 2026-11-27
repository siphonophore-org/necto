using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Hands the script filters the groups, once the server starts, so a script runs for a user only when it runs for
/// every user or names one of the user's groups. It also moves the scripts stored groups ran onto the scripts.
/// </summary>
public sealed class ScriptGroups : IHostedService
{
    private readonly PermissionService _permissions;
    private readonly IConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptGroups"/> class.
    /// </summary>
    /// <param name="permissions">Tells whether a user is an administrator.</param>
    /// <param name="store">The configuration that holds the scripts and the groups.</param>
    public ScriptGroups(PermissionService permissions, IConfigurationStore store)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Works out whether a script runs for a user. Administrators run every script, and an event with no user runs a
    /// script that runs for every user or names Everyone.
    /// </summary>
    /// <param name="userId">The user, or <see cref="Guid.Empty"/> for an event with no user.</param>
    /// <param name="scriptId">The script.</param>
    /// <returns><c>true</c> when the script runs for the user.</returns>
    public bool RunsFor(Guid userId, Guid scriptId)
    {
        if (userId != Guid.Empty && _permissions.For(userId).IsAdministrator)
        {
            return true;
        }

        return _store.Read(c =>
        {
            var script = c.Scripts?.Scripts?.Find(s => s?.Id == scriptId);
            // A script not saved yet, such as one under test, runs for every user as a new script does.
            if (script is null)
            {
                return true;
            }

            var groups = userId == Guid.Empty ? new List<Guid> { Group.EveryoneId } : GroupMembership.GroupIdsOf(c, userId);
            return ScriptGroupFilter.Runs(script, groups);
        });
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _store.Mutate(c => ScriptGroupFilter.Migrate(c));
        ConditionEvaluator.RunsFor = RunsFor;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        ConditionEvaluator.RunsFor = null;
        return Task.CompletedTask;
    }
}
