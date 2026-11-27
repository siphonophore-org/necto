using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Siphonophore.Stolon;

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

    /// <summary>
    /// Gets every Requests event, in the order a request lives through them. The requester and those who joined get
    /// each request's own events, and the reminders go only to those who approve requests.
    /// </summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Created, TranslationKeys.RequestsEventCreated, ForUsers: true),
        new NectoEventInfo(Approved, TranslationKeys.RequestsActivityApprovedName, ForUsers: true),
        new NectoEventInfo(Declined, TranslationKeys.RequestsActivityRejectedName, ForUsers: true),
        new NectoEventInfo(Available, TranslationKeys.RequestsActivitySummaryAvailableOne, ForUsers: true),
        new NectoEventInfo(Failed, TranslationKeys.RequestsActivitySummaryNotSentOne, ForUsers: true),
        new NectoEventInfo(Aging, TranslationKeys.RequestsEventAging),
        new NectoEventInfo(Expiring, TranslationKeys.RequestsEventExpiring)
    };

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
