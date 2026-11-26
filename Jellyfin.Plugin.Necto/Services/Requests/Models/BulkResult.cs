using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// How a bulk action went.
/// </summary>
public class BulkResult
{
    /// <summary>Gets or sets how many requests it applied to.</summary>
    public int Done { get; set; }

    /// <summary>Gets or sets how many it could not apply to, because they were gone or in the wrong state.</summary>
    public int Skipped { get; set; }

    /// <summary>Gets or sets how many approvals were refused by Radarr or Sonarr.</summary>
    public int Failed { get; set; }
}
