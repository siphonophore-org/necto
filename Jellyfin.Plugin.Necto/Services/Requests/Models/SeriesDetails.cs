using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The seasons of a show, for choosing which to request.
/// </summary>
public class SeriesDetails
{
    /// <summary>Gets or sets the seasons, in order. Empty when they could not be listed.</summary>
    public List<SeasonInfo> Seasons { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether single seasons can be requested (Sonarr knows the show).</summary>
    public bool CanPickSeasons { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only the whole show can be requested (the Full series only setting):
    /// every season the library doesn't have, none picked.
    /// </summary>
    public bool WholeShowOnly { get; set; }

    /// <summary>Gets or sets the show's specials, each to request on its own, where the server allows them; none otherwise.</summary>
    public List<SpecialEpisode> Specials { get; set; } = new();

    /// <summary>Gets or sets the library series, when the library has the show.</summary>
    public Guid? LibraryItemId { get; set; }

    /// <summary>Gets or sets a value indicating whether an open request covers the whole show.</summary>
    public bool WholeShowRequested { get; set; }
}
