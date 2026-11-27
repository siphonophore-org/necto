using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The body of a change to a request still waiting for approval: what its requester can pick again.
/// </summary>
public class MediaRequestEdit
{
    /// <summary>Gets or sets the seasons of a show to request; null or empty for the whole show. Ignored for a movie.</summary>
    public List<int>? Seasons { get; set; }

    /// <summary>Gets or sets the profile picked, from the ones the requester may pick; empty for their own.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Gets or sets a note for the administrators, if any.</summary>
    public string? Note { get; set; }
}
