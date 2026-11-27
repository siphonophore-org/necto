using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The scripts a stored group runs for its members. Each script holds its own groups, and
/// <see cref="Services.Scripts.ScriptGroupFilter.Migrate"/> moves these onto the scripts and clears them.
/// </summary>
public class ScriptPermissions
{
    /// <summary>Gets or sets a value indicating whether every script runs for members.</summary>
    public bool AllScripts { get; set; }

    /// <summary>Gets or sets the ids of the scripts that run for members.</summary>
    public List<string> ScriptIds { get; set; } = new();
}
