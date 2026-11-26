using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Where a request stands.
/// </summary>
public enum RequestStatus
{
    /// <summary>Waiting for an administrator.</summary>
    Pending = 0,

    /// <summary>Approved, and handed to Radarr or Sonarr when one is set up.</summary>
    Approved = 1,

    /// <summary>Turned down by an administrator.</summary>
    Declined = 2,

    /// <summary>In the library.</summary>
    Available = 3,

    /// <summary>Approved, but Radarr or Sonarr refused it. It is retried on a schedule, and approving again retries now.</summary>
    Failed = 4,

    /// <summary>A show with some of its requested seasons in the library.</summary>
    PartiallyAvailable = 5
}
