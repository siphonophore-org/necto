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

    /// <summary>Gets every Issues event key.</summary>
    public static IReadOnlyList<string> All { get; } = new[] { Created, Updated };

    /// <summary>
    /// Gets the name of an event, in the server's display language, for Notifications to list it by.
    /// </summary>
    /// <param name="key">The event's key.</param>
    /// <returns>The name, or the key for one Issues does not send.</returns>
    public static string Name(string key) => key switch
    {
        Created => Requests.ServerWording.Get(TranslationKeys.IssuesEventReported),
        Updated => Requests.ServerWording.Get(TranslationKeys.IssuesEventResolved),
        _ => key
    };

    /// <summary>
    /// Gets the key of an issue's event.
    /// </summary>
    /// <param name="what">What happened.</param>
    /// <returns>The key.</returns>
    public static string KeyFor(NotificationEvent what) => what == NotificationEvent.IssueReported ? Created : Updated;
}
