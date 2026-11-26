using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The scripts that run for members.
/// </summary>
public class ScriptPermissions
{
    /// <summary>Gets or sets a value indicating whether every script runs for members.</summary>
    public bool AllScripts { get; set; }

    /// <summary>Gets or sets the ids of the scripts that run for members.</summary>
    public List<string> ScriptIds { get; set; } = new();
}
