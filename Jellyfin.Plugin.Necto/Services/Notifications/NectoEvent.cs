using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One thing a tool reports to Notifications, such as a new request or a missing disk.
/// </summary>
/// <param name="Key">The event's key, such as <c>requests.created</c>. The part before the dot is the tool's key.</param>
/// <param name="Title">The short line a person reads first.</param>
/// <param name="Body">The detail under the title, or empty.</param>
public sealed record NectoEvent(string Key, string Title, string Body)
{
    /// <summary>Gets the user the event is about, when there is one. That user receives it if their groups allow.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Gets the page a notification opens, relative to the server, or <c>null</c>.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the event's level, which a channel may use to pick a color or skip quiet events.</summary>
    public NectoEventLevel Level { get; init; } = NectoEventLevel.Info;

    /// <summary>Gets extra values a webhook carries.</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}
