using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Scripts;

/// <summary>
/// The scripts and the Scripts tool's settings. Which scripts run for a user is set on groups.
/// </summary>
public class ScriptSettings
{
    /// <summary>Gets or sets the configured scripts.</summary>
    public List<ScriptDefinition> Scripts { get; set; } = new();

    /// <summary>Gets or sets how many runs may execute at once. Further runs wait their turn.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>Gets or sets the timeout for a command that sets none, in seconds.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 60;

    /// <summary>Gets or sets how many runs the history keeps.</summary>
    public int HistorySize { get; set; } = 200;
}
