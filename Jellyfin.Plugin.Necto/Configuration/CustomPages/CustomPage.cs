using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.CustomPages;

/// <summary>
/// One page an administrator wrote, served at <c>/pages/{slug}</c>. Who may open a signed in page comes from the
/// groups that give its <see cref="Id"/>.
/// </summary>
public class CustomPage
{
    /// <summary>Gets or sets the page's stable id, which groups name. It survives a change of slug.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the slug the page is served at, the <c>abc</c> in <c>/pages/abc</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Gets or sets the document title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets who may open the page.</summary>
    public PageVisibility Visibility { get; set; } = PageVisibility.Anonymous;

    /// <summary>Gets or sets the body markup.</summary>
    public string Html { get; set; } = string.Empty;

    /// <summary>Gets or sets the styles, placed in a <c>style</c> element.</summary>
    public string Css { get; set; } = string.Empty;

    /// <summary>Gets or sets the script, placed in a <c>script</c> element.</summary>
    public string Js { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the page is one complete document. When <c>true</c>,
    /// <see cref="Document"/> is served as written and <see cref="Html"/>, <see cref="Css"/>, and <see cref="Js"/> are ignored.
    /// </summary>
    public bool SingleFile { get; set; }

    /// <summary>Gets or sets the complete document served while <see cref="SingleFile"/> is <c>true</c>.</summary>
    public string Document { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the page is published. An unpublished page answers 404.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the page runs without the sandbox, on the Jellyfin origin with the reach
    /// of the web client, so it can call the server and read the viewer's session.
    /// </summary>
    public bool Unsandboxed { get; set; }

    /// <summary>
    /// Gets or sets the server side routes the page calls through <c>serverFetch</c>. A page with routes runs without
    /// the sandbox, since the helper calls the route on the Jellyfin origin.
    /// </summary>
    public List<PageApiRoute> ApiRoutes { get; set; } = new();
}
