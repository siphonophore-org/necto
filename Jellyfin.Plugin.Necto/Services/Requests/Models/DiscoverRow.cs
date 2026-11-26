using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A row of the empty search: one of TMDb's trending or upcoming lists, the start of it, its titles marked for the
/// viewer as search results are.
/// </summary>
public class DiscoverRow
{
    /// <summary>Gets or sets the row's key, such as TrendingShows or UpcomingMovies.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the row's heading, such as Trending Movies.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the row's titles, in order.</summary>
    public List<SearchResult> Items { get; set; } = new();
}
