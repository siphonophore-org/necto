using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Something a title can be browsed by: one of its genres, studios or networks, with the id TMDb lists titles by.
/// </summary>
public class BrowseLink
{
    /// <summary>A genre.</summary>
    public const string Genre = "Genre";

    /// <summary>A studio, or a show's production company.</summary>
    public const string Company = "Company";

    /// <summary>A show's network.</summary>
    public const string Network = "Network";

    /// <summary>A TMDb collection of movies, such as Dune Collection.</summary>
    public const string Collection = "Collection";

    /// <summary>Gets or sets what it is: Genre, Company, Network or Collection.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets its TMDb id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; } = string.Empty;
}
