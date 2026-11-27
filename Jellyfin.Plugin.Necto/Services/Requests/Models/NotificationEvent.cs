using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The moments in a request's life that are sent as updates.
/// </summary>
public enum NotificationEvent
{
    /// <summary>A request was made and waits for approval.</summary>
    Created = 0,

    /// <summary>A request was approved.</summary>
    Approved = 1,

    /// <summary>A request was declined.</summary>
    Declined = 2,

    /// <summary>A requested title reached the library.</summary>
    Available = 3,

    /// <summary>Radarr or Sonarr refused a request.</summary>
    Failed = 4,

    /// <summary>A request has waited for approval longer than the aging setting allows. Sent once, to administrators.</summary>
    Aging = 5,

    /// <summary>A waiting request is rejected automatically within three days. Sent once a day, to administrators.</summary>
    Expiring = 6,

    /// <summary>Someone reported an issue with a library item. Sent to administrators by default.</summary>
    IssueReported = 7,

    /// <summary>An administrator resolved or declined an issue. Sent to its reporters by default.</summary>
    IssueResolved = 8
}
