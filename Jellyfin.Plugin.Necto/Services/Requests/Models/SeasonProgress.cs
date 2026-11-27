using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Where one requested season of a show stands in Sonarr: its episodes on disk, how many it has, and how many are
/// downloading.
/// </summary>
public class SeasonProgress
{
    /// <summary>Gets or sets the season number.</summary>
    public int Season { get; set; }

    /// <summary>Gets or sets how many of its episodes Sonarr has files for.</summary>
    public int Files { get; set; }

    /// <summary>Gets or sets how many episodes it has.</summary>
    public int Episodes { get; set; }

    /// <summary>Gets or sets how many of its episodes are downloading.</summary>
    public int Downloading { get; set; }
}
