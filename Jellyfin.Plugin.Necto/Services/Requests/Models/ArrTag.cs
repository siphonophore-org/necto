using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A Radarr or Sonarr tag, which a profile can give the titles it adds.
/// </summary>
public class ArrTag
{
    /// <summary>Gets or sets the tag's id on its server.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the tag's label.</summary>
    public string Label { get; set; } = string.Empty;
}
