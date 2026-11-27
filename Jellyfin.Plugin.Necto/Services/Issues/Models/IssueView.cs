using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// What the issue page shows for one item: the item, the open issue the viewer reported or joined, someone else's
/// open issue, and the closed ones.
/// </summary>
public class IssueView
{
    /// <summary>Gets or sets the item.</summary>
    public IssueItem Item { get; set; } = new();

    /// <summary>Gets or sets the open issue the viewer reported or joined, or null.</summary>
    public Issue? Mine { get; set; }

    /// <summary>Gets or sets an open issue someone else reported, which the viewer can join, or null.</summary>
    public Issue? Open { get; set; }

    /// <summary>Gets or sets the item's closed issues, newest first.</summary>
    public List<Issue> Closed { get; set; } = new();

    /// <summary>Gets or sets why the viewer cannot report or join an issue now, or null when they can.</summary>
    public string? Refusal { get; set; }
}
