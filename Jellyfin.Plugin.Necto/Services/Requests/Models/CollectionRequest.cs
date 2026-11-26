using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// How a collection's movies are requested: the profile and note each gets, as the request form gives them.
/// </summary>
public class CollectionRequest
{
    /// <summary>Gets or sets the profile, from the ones the requester sees; the first of them when empty.</summary>
    public string? ProfileId { get; set; }

    /// <summary>Gets or sets the requester's note.</summary>
    public string? Note { get; set; }
}
