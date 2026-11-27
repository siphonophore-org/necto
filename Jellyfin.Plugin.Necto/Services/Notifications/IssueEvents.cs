using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The events Issues sends, so Notifications can list them.
/// </summary>
public static class IssueEvents
{
    /// <summary>Someone reported an issue with a library item.</summary>
    public const string Created = "issues.created";

    /// <summary>An issue was resolved or declined.</summary>
    public const string Updated = "issues.updated";

    /// <summary>Gets every Issues event. Each goes to the issue's reporters.</summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Created, TranslationKeys.IssuesEventReported, ForUsers: true),
        new NectoEventInfo(Updated, TranslationKeys.IssuesEventResolved, ForUsers: true)
    };

    /// <summary>
    /// Gets the key of an issue's event.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(NotificationEvent what) => what == NotificationEvent.IssueReported ? Created : Updated;
}
