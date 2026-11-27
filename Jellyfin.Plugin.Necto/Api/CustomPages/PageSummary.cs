using Jellyfin.Plugin.Necto.Configuration.CustomPages;

namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// One page as <c>GET /Pages</c> lists it, for the group editor.
/// </summary>
public class PageSummary
{
    /// <summary>Gets or sets the page's id, which groups name.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the page's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the page's slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Gets or sets who may open the page.</summary>
    public PageVisibility Visibility { get; set; }

    /// <summary>Gets or sets a value indicating whether the page is published.</summary>
    public bool Enabled { get; set; }
}
