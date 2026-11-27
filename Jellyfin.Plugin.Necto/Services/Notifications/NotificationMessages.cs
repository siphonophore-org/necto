using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Writes an event as the message each channel sends, and as the text of an email or an activity entry.
/// </summary>
public static class NotificationMessages
{
    /// <summary>The color of an informational event's Discord embed.</summary>
    public const int InfoColor = 0x00A4DC;

    /// <summary>The color of a warning's Discord embed.</summary>
    public const int WarningColor = 0xFF9800;

    /// <summary>The color of a failure's Discord embed.</summary>
    public const int ErrorColor = 0xF44336;

    /// <summary>
    /// Gets the color of an event's Discord embed.
    /// </summary>
    /// <param name="level">The event's level.</param>
    /// <returns>The color, as 0xRRGGBB.</returns>
    public static int Color(NectoEventLevel level) => level switch
    {
        NectoEventLevel.Warning => WarningColor,
        NectoEventLevel.Error => ErrorColor,
        _ => InfoColor
    };

    /// <summary>
    /// Makes an event's address absolute with the server's public address, so a service outside the server can open it.
    /// </summary>
    /// <param name="url">The event's address, relative to the server or absolute.</param>
    /// <param name="serverUrl">The server's public address, or empty.</param>
    /// <returns>The absolute http or https address, the relative one when no server address is set, or <c>null</c>.</returns>
    public static string? Link(string? url, string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/') + "/web/";
        }

        url = url.Trim();
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !url.StartsWith('/'))
        {
            // Any other scheme, such as javascript:, could run in whatever opens the link.
            return absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp ? absolute.AbsoluteUri : null;
        }

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return url;
        }

        var joined = serverUrl.TrimEnd('/') + "/" + url.TrimStart('/');
        return Uri.TryCreate(joined, UriKind.Absolute, out var full) && (full.Scheme == Uri.UriSchemeHttps || full.Scheme == Uri.UriSchemeHttp) ? full.AbsoluteUri : null;
    }

    /// <summary>
    /// Writes an event as a Stolon message, with its placeholders, its color, its link, and its data.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="settings">The Notifications settings, for the server's address.</param>
    /// <param name="eventName">The event's name, such as Invite used.</param>
    /// <param name="now">When it is sent.</param>
    /// <param name="language">The language the event is written in, or <c>null</c>.</param>
    /// <returns>The message, with no recipients.</returns>
    public static NotifyMessage Build(NectoEvent nectoEvent, NotificationSettings? settings, string eventName, DateTimeOffset now, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        var link = Link(nectoEvent.Url, settings?.ServerUrl);
        var message = new NotifyMessage
        {
            Title = "{title}",
            Text = "{body}",
            LinkUrl = link,
            ThumbnailUrl = nectoEvent.ImageUrl,
            OneDevice = nectoEvent.OneDevice,
            Hold = nectoEvent.Hold,
            Color = Color(nectoEvent.Level),
            Timestamp = now,
            Language = language,
            Tag = string.IsNullOrWhiteSpace(nectoEvent.Tag) ? "necto-" + nectoEvent.Key : nectoEvent.Tag
        };

        message.Values["title"] = nectoEvent.Title ?? string.Empty;
        message.Values["body"] = nectoEvent.Body ?? string.Empty;
        message.Values["event"] = nectoEvent.Key;
        message.Values["eventName"] = eventName ?? string.Empty;
        message.Values["level"] = nectoEvent.Level.ToString();
        message.Values["link"] = link ?? string.Empty;
        message.Values["time"] = now.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        message.Values["color"] = Color(nectoEvent.Level).ToString(CultureInfo.InvariantCulture);
        message.Values["serverURL"] = settings?.ServerUrl ?? string.Empty;
        message.Values["userId"] = nectoEvent.UserId is { } user ? user.ToString("N", CultureInfo.InvariantCulture) : string.Empty;
        foreach (var key in new[] { "event", "level", "link", "time", "color", "serverURL", "userId" })
        {
            message.PlainKeys.Add(key);
        }

        message.Data["event"] = nectoEvent.Key;
        message.Data["eventName"] = eventName ?? string.Empty;
        message.Data["level"] = nectoEvent.Level.ToString();
        if (nectoEvent.UserId is { } id)
        {
            message.Data["userId"] = id.ToString("N", CultureInfo.InvariantCulture);
        }

        foreach (var (key, value) in (nectoEvent.Data ?? new System.Collections.Generic.Dictionary<string, string>()).Where(p => !string.IsNullOrEmpty(p.Key)))
        {
            message.Data[key] = value;
            if (!message.Values.ContainsKey(key))
            {
                message.Values[key] = value ?? string.Empty;
            }
        }

        foreach (var (key, value) in (nectoEvent.Values ?? new System.Collections.Generic.Dictionary<string, string>()).Where(p => !string.IsNullOrEmpty(p.Key)))
        {
            if (!message.Values.ContainsKey(key))
            {
                message.Values[key] = value ?? string.Empty;
            }
        }

        return message;
    }

    /// <summary>
    /// Picks the version of an event a channel sends: the event told without naming its user when the channel leaves
    /// the user out and the event has such a version, or else the event itself.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="channel">The channel, or <c>null</c>.</param>
    /// <returns>The event to send.</returns>
    public static NectoEvent For(NectoEvent nectoEvent, NotificationChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        return channel is not null && !channel.Shows(PostFields.User) && nectoEvent.Anonymous is { } anonymous ? anonymous : nectoEvent;
    }

    /// <summary>
    /// Writes an event as a Discord or webhook post, with the fields, poster, and time the channel carries. A custom
    /// body gets every field as a placeholder.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="channel">The Discord or webhook channel.</param>
    /// <param name="settings">The Notifications settings, for the server's address.</param>
    /// <param name="eventName">The event's name.</param>
    /// <param name="now">When it is sent.</param>
    /// <param name="language">The language the event is written in, or <c>null</c>.</param>
    /// <returns>The message, with no recipients.</returns>
    public static NotifyMessage Post(NectoEvent nectoEvent, NotificationChannel channel, NotificationSettings? settings, string eventName, DateTimeOffset now, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var sent = For(nectoEvent, channel);
        var message = Build(sent, settings, eventName, now, language);
        var fields = sent.Fields ?? Array.Empty<NectoEventField>();
        var discord = channel.Kind == NotificationChannelKind.Discord;
        message.Values[PostFields.Poster] = sent.ImageUrl ?? string.Empty;
        message.PlainKeys.Add(PostFields.Poster);
        foreach (var field in fields.Where(f => !f.Json && !message.Values.ContainsKey(f.Key)))
        {
            message.Values[field.Key] = field.Value ?? string.Empty;
        }

        foreach (var field in fields.Where(f => f.Json && !string.IsNullOrWhiteSpace(f.Value) && (channel.UseCustomBody || (!discord && channel.Shows(f.Key)))))
        {
            message.Data[field.Key] = JsonNode.Parse(field.Value);
        }

        if (channel.UseCustomBody)
        {
            return message;
        }

        if (!channel.Shows(PostFields.Poster))
        {
            message.ThumbnailUrl = null;
        }

        if (!channel.Shows(PostFields.Time))
        {
            message.Timestamp = null;
        }

        foreach (var field in fields.Where(f => !f.Json && channel.Shows(f.Key)))
        {
            var value = (field.Value ?? string.Empty).Trim();
            if (!discord)
            {
                message.Data[field.Key] = value.Length == 0 ? null : value;
            }
            else if (field.InText)
            {
                message.Text += value.Length == 0 ? string.Empty : "\n\n{" + field.Key + "}";
            }
            else
            {
                message.Fields.Add(new NotifyField(field.Name ?? string.Empty, value, field.Inline));
            }
        }

        if (!discord && channel.Shows(PostFields.Poster) && sent.ImageUrl is { Length: > 0 } poster)
        {
            message.Data[PostFields.Poster] = poster;
        }

        return message;
    }

    /// <summary>
    /// Writes an event as an email.
    /// </summary>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="settings">The Notifications settings, for the server's address.</param>
    /// <returns>The subject and the text, with the link under the body.</returns>
    public static (string Subject, string Body) Email(NectoEvent nectoEvent, NotificationSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        var link = Link(nectoEvent.Url, settings?.ServerUrl);
        var body = (nectoEvent.Body ?? string.Empty).Trim();
        if (link is not null && Uri.IsWellFormedUriString(link, UriKind.Absolute))
        {
            body = body.Length == 0 ? link : body + "\n\n" + link;
        }

        return (NotifyTemplate.Clip(NotifyTemplate.StripMarkup(nectoEvent.Title ?? string.Empty).Replace('\n', ' ').Replace('\r', ' '), 200), body);
    }
}
