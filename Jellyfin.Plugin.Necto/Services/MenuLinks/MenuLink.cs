using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.MenuLinks;

/// <summary>A custom link in the web client's side menu, as config.json's menuLinks holds it.</summary>
public class MenuLink
{
    /// <summary>Gets or sets the name the menu shows.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Material icon's name, such as link. Empty shows the web client's default.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Gets or sets the address it opens.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool that keeps this link, requests or issues, while that tool is on, or null. A kept link
    /// cannot be removed, and its name and icon are remembered for when Necto adds it again.
    /// </summary>
    public string? Managed { get; set; }
}

/// <summary>The web client's menu links and whether the editor can change them.</summary>
public class MenuLinksView
{
    /// <summary>Gets or sets the links, in their order.</summary>
    public List<MenuLink> Links { get; set; } = new();

    /// <summary>Gets or sets where config.json is, or empty when Jellyfin serves no web client.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the links can be saved.</summary>
    public bool Writable { get; set; }

    /// <summary>Gets or sets why the links cannot be read or saved, as a sentence, or empty.</summary>
    public string Problem { get; set; } = string.Empty;
}
