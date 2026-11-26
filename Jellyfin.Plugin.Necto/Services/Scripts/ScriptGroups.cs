using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Services.Configuration;
using Jellyfin.Plugin.Necto.Services.Groups;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Hands the script filters the groups, once the server starts, so a script runs for a user only when one of the
/// user's groups runs it.
/// </summary>
public sealed class ScriptGroups : IHostedService
{
    private readonly PermissionService _permissions;
    private readonly IConfigurationStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptGroups"/> class.
    /// </summary>
    /// <param name="permissions">Gives what a user may do.</param>
    /// <param name="store">Reads the Everyone group.</param>
    public ScriptGroups(PermissionService permissions, IConfigurationStore store)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Works out whether a script runs for a user. An empty user, for an event about a name the server does not have,
    /// takes the Everyone group.
    /// </summary>
    /// <param name="userId">The user, or empty.</param>
    /// <param name="scriptId">The script.</param>
    /// <returns><c>true</c> when the user's groups run the script.</returns>
    public bool RunsFor(Guid userId, Guid scriptId)
    {
        if (userId == Guid.Empty)
        {
            return _store.Read(c => Runs(c.Everyone.Permissions.Scripts, scriptId));
        }

        var permissions = _permissions.For(userId);
        return permissions.IsAdministrator || Runs(permissions.Scripts, scriptId);
    }

    /// <summary>
    /// Works out whether a group's Scripts section runs a script. Ids match with or without dashes.
    /// </summary>
    /// <param name="scripts">The section.</param>
    /// <param name="scriptId">The script.</param>
    /// <returns><c>true</c> when the section runs every script or names this one.</returns>
    public static bool Runs(ScriptPermissions scripts, Guid scriptId)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        return scripts.AllScripts || scripts.ScriptIds.Exists(id => Guid.TryParse(id, out var g) && g == scriptId);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
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
