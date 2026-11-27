using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;

namespace Jellyfin.Plugin.Necto.Services.Issues.Models;

/// <summary>
/// Where an issue stands. Reported and Confirmed are open, the rest are closed.
/// </summary>
public enum IssueState
{
    /// <summary>Reported, and not yet looked at by an administrator.</summary>
    Reported = 0,

    /// <summary>An administrator agrees something is wrong.</summary>
    Confirmed = 1,

    /// <summary>An administrator fixed it.</summary>
    Resolved = 2,

    /// <summary>An administrator found nothing to fix.</summary>
    Declined = 3,

    /// <summary>Its only reporter took it back.</summary>
    Rescinded = 4
}
