using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A Radarr or Sonarr quality profile.
/// </summary>
public class ArrProfile
{
    /// <summary>Gets or sets the profile id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the profile name.</summary>
    public string Name { get; set; } = string.Empty;
}
