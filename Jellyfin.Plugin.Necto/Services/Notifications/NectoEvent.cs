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

    /// <summary>Gets more users the event is about, such as those who joined a request. Each receives it if their groups allow.</summary>
    public IReadOnlyList<Guid> OtherUserIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Gets users the Administrators channel and the groups leave out, such as the administrator who made a decision.</summary>
    public IReadOnlyList<Guid> ExceptUserIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Gets a value indicating whether a Jellyfin message for a user with no app online waits until one comes online.</summary>
    public bool Hold { get; init; } = true;

    /// <summary>Gets a value indicating whether a Jellyfin message shows on each user's most recent app only, such as a reminder.</summary>
    public bool OneDevice { get; init; }

    /// <summary>Gets the absolute http or https address of a small image, such as a poster, for Discord and browser notifications.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Gets who the event goes to: its users, its channels, or both.</summary>
    public NectoEventAudience Audience { get; init; } = NectoEventAudience.All;

    /// <summary>Gets values only a custom body's placeholders read, which a built webhook post leaves out.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the fields a Discord or webhook post carries, each as the channel's post settings allow.</summary>
    public IReadOnlyList<NectoEventField> Fields { get; init; } = Array.Empty<NectoEventField>();

    /// <summary>Gets the event told without naming its user, which a channel that leaves the user out sends, or <c>null</c>.</summary>
    public NectoEvent? Anonymous { get; init; }

    /// <summary>Gets the tag a browser notification replaces an older one with, such as one request's, or <c>null</c> for the event's key.</summary>
    public string? Tag { get; init; }

    /// <summary>Gets what the event is about, such as one request, so held Jellyfin messages about it can be dropped once it is gone.</summary>
    public string? Subject { get; init; }
}
