namespace Jellyfin.Plugin.Necto.Configuration.Notifications;

/// <summary>
/// The keys of the post fields a channel can leave out, as <see cref="NotificationChannel.Shows"/> reads them.
/// </summary>
public static class PostFields
{
    /// <summary>The poster, as a Discord thumbnail and a webhook's <c>poster</c>.</summary>
    public const string Poster = "poster";

    /// <summary>The title's summary.</summary>
    public const string Overview = "overview";

    /// <summary>The requester or reporter. Left out, a post names no one.</summary>
    public const string User = "user";

    /// <summary>How many others joined.</summary>
    public const string Joined = "joined";

    /// <summary>The seasons asked for.</summary>
    public const string Seasons = "seasons";

    /// <summary>The requester's or reporter's note.</summary>
    public const string Note = "note";

    /// <summary>When it happened.</summary>
    public const string Time = "time";

    /// <summary>The whole request, as JSON in a webhook post.</summary>
    public const string Request = "request";

    /// <summary>The whole issue, as JSON in a webhook post.</summary>
    public const string Issue = "issue";
}
