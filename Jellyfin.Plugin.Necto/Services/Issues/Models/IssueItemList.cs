using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// The playable items the issue page lists: its suggestions, or what a search found.
/// </summary>
public class IssueItemList
{
    /// <summary>Gets or sets the items.</summary>
    public List<IssueItem> Items { get; set; } = new();
}
