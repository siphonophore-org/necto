using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests.Models;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The events Requests sends, so Notifications can list them.
/// </summary>
public static class RequestEvents
{
    /// <summary>A request was made and waits for approval.</summary>
    public const string Created = "requests.created";

    /// <summary>A request was approved.</summary>
    public const string Approved = "requests.approved";

    /// <summary>A request was declined.</summary>
    public const string Declined = "requests.declined";

    /// <summary>A requested title reached the library.</summary>
    public const string Available = "requests.available";

    /// <summary>Radarr or Sonarr refused a request, which is tried again.</summary>
    public const string Failed = "requests.failed";

    /// <summary>A request waited for approval longer than Settings allows. Sent once.</summary>
    public const string Aging = "requests.aging";

    /// <summary>A waiting request is declined on its own within three days. Sent once a day.</summary>
    public const string Expiring = "requests.expiring";

    /// <summary>Gets every Requests event key, in the order a request lives through them.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Created, Approved, Declined, Available, Failed, Aging, Expiring };

    /// <summary>
    /// Gets the key of a request's event.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(NotificationEvent what) => what switch
    {
        NotificationEvent.Created => Created,
        NotificationEvent.Approved => Approved,
        NotificationEvent.Declined => Declined,
        NotificationEvent.Available => Available,
        NotificationEvent.Aging => Aging,
        NotificationEvent.Expiring => Expiring,
        _ => Failed
    };
}
