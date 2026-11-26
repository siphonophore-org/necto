using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A TMDb collection of movies, such as Dune Collection, with each movie as search results show them.
/// </summary>
public class CollectionDto
{
    /// <summary>Gets or sets the collection's TMDb id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets what it is.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets its poster.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets its backdrop.</summary>
    public string? BackdropUrl { get; set; }

    /// <summary>Gets or sets how many of its movies can be requested: not in the library, requested or on their way.</summary>
    public int Requestable { get; set; }

    /// <summary>Gets or sets its movies, in release order.</summary>
    public List<SearchItemDto> Items { get; set; } = new();
}
