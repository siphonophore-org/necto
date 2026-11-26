using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The custom pages members may open.
/// </summary>
public class CustomPagePermissions
{
    /// <summary>Gets or sets a value indicating whether members may open every page.</summary>
    public bool AllPages { get; set; }

    /// <summary>Gets or sets the ids of the pages members may open.</summary>
    public List<string> PageIds { get; set; } = new();
}
