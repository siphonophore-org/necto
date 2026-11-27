using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The person a page asks about, by name and, when known, TMDb id and photo.
/// </summary>
public class PersonLookup
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the TMDb id.</summary>
    public string? TmdbId { get; set; }

    /// <summary>Gets or sets a photo address already known to the page.</summary>
    public string? ImageUrl { get; set; }
}
