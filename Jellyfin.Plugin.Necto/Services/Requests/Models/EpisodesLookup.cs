using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A season of a show to list the episodes of: the show by its ids, title and year, and the season's number.
/// </summary>
public class EpisodesLookup : SeriesLookup
{
    /// <summary>Gets or sets the season's number.</summary>
    public int Season { get; set; }
}
