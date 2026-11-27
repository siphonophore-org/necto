using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The body of a new request, as picked from search results.
/// </summary>
public class NewMediaRequest
{
    /// <summary>Gets or sets what is requested.</summary>
    public RequestType Type { get; set; }

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the release year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the overview.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the poster address.</summary>
    public string? PosterUrl { get; set; }

    /// <summary>Gets or sets the backdrop address.</summary>
    public string? BackdropUrl { get; set; }

    /// <summary>Gets or sets the provider ids.</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the seasons of a show to request; null or empty for the whole show.</summary>
    public List<int>? Seasons { get; set; }

    /// <summary>
    /// Gets or sets the specials to request, by their numbers on TMDb, where the server allows them: a request of its
    /// own, sent without <see cref="Seasons"/>.
    /// </summary>
    public List<int>? Specials { get; set; }

    /// <summary>Gets or sets a note for the administrators, if any.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets the profile picked, from the ones the requester may pick; empty for their own.</summary>
    public string? ProfileId { get; set; }
}
