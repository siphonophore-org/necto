using System;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One of a show's specials (an episode of its season 0), as a request asks for it and the request form lists it.
/// </summary>
public class SpecialEpisode
{
    /// <summary>Gets or sets its number among the show's specials, as TMDb numbers them.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets its name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets when it aired, which Sonarr and the library find it by where they number specials differently.</summary>
    public DateTime? AirDate { get; set; }

    /// <summary>Gets or sets a value indicating whether the library has it, for the request form.</summary>
    public bool InLibrary { get; set; }

    /// <summary>Gets or sets a value indicating whether an open request already asks for it, for the request form.</summary>
    public bool Requested { get; set; }
}
