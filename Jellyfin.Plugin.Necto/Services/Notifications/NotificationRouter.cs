using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Decides where an event goes. The user it is about gets a user facing event in their browsers and on their Jellyfin
/// apps when their groups give them that event, and every administrators' channel that picks the event gets it.
/// </summary>
public static class NotificationRouter
{
    /// <summary>The id of the user channel for browser notifications.</summary>
    public const string BrowsersId = "browsers";

    /// <summary>The id of the user channel for Jellyfin app messages.</summary>
    public const string AppsId = "apps";

    /// <summary>
    /// Plans every send of an event.
    /// </summary>
    /// <param name="config">The configuration, for the channels and the groups.</param>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="users">Every user on the server.</param>
    /// <returns>The sends, the user's first, then the administrators' channels in order.</returns>
    public static IReadOnlyList<NotificationSend> Plan(PluginConfiguration config, NectoEvent nectoEvent, IReadOnlyCollection<NotificationUser> users)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(nectoEvent);
        ArgumentNullException.ThrowIfNull(users);
        var settings = config.Notifications ?? new NotificationSettings();
        var sends = new List<NotificationSend>();

        var people = UserRecipients(config, nectoEvent, users);
        if (people.Count > 0)
        {
            if (settings.BrowserNotifications)
            {
                sends.Add(new NotificationSend(NotificationTarget.UserBrowsers, BrowsersId, people));
            }

            if (settings.JellyfinMessages)
            {
                sends.Add(new NotificationSend(NotificationTarget.UserApps, AppsId, people));
            }
        }

        var reached = sends.Count > 0 ? people : Array.Empty<Guid>();
        foreach (var channel in (settings.Channels ?? new List<NotificationChannel>()).Where(c => c is not null && c.Takes(nectoEvent.Key)))
        {
            switch (channel.Kind)
            {
                case NotificationChannelKind.Administrators:
                    // An administrator the event is about already has it as a user, so it comes once.
                    var admins = users.Where(u => u.IsAdministrator && !u.IsDisabled && !reached.Contains(u.Id)).Select(u => u.Id).ToList();
                    if (admins.Count > 0)
                    {
                        sends.Add(new NotificationSend(NotificationTarget.Administrators, channel.Id, admins));
                    }

                    break;
                case NotificationChannelKind.Discord:
                    sends.Add(new NotificationSend(NotificationTarget.Discord, channel.Id, Array.Empty<Guid>()));
                    break;
                case NotificationChannelKind.Webhook:
                    sends.Add(new NotificationSend(NotificationTarget.Webhook, channel.Id, Array.Empty<Guid>()));
                    break;
                case NotificationChannelKind.Email:
                    sends.Add(new NotificationSend(NotificationTarget.Email, channel.Id, Array.Empty<Guid>()));
                    break;
                case NotificationChannelKind.ActivityLog:
                    sends.Add(new NotificationSend(NotificationTarget.ActivityLog, channel.Id, Array.Empty<Guid>()));
                    break;
            }
        }

        return sends;
    }

    /// <summary>
    /// Finds the users a user facing event goes to: each user it is about, when the account works and their groups give
    /// them the event.
    /// </summary>
    /// <param name="config">The configuration, for the groups.</param>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="users">Every user on the server.</param>
    /// <returns>The users, each once, the event's own user first.</returns>
    public static IReadOnlyList<Guid> UserRecipients(PluginConfiguration config, NectoEvent nectoEvent, IReadOnlyCollection<NotificationUser> users)
    {
        ArgumentNullException.ThrowIfNull(nectoEvent);
        var ids = (nectoEvent.UserId is { } first ? new[] { first } : Array.Empty<Guid>()).Concat(nectoEvent.OtherUserIds ?? Array.Empty<Guid>()).Distinct();
        return ids.Select(id => UserRecipient(config, nectoEvent with { UserId = id }, users)).OfType<Guid>().ToList();
    }

    /// <summary>
    /// Finds whether the user an event names gets it: when the event is user facing, the account works, and their
    /// groups give them the event.
    /// </summary>
    /// <param name="config">The configuration, for the groups.</param>
    /// <param name="nectoEvent">The event.</param>
    /// <param name="users">Every user on the server.</param>
    /// <returns>The user, or <c>null</c> when nobody gets it as a user.</returns>
    public static Guid? UserRecipient(PluginConfiguration config, NectoEvent nectoEvent, IReadOnlyCollection<NotificationUser> users)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(nectoEvent);
        ArgumentNullException.ThrowIfNull(users);
        var info = NectoEventCatalog.Find(nectoEvent.Key);
        if (info is not { ForUsers: true } || nectoEvent.UserId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        var user = users.FirstOrDefault(u => u.Id == id);
        if (user is null || user.IsDisabled)
        {
            return null;
        }

        return PermissionService.Resolve(config, id, user.IsAdministrator).Receives(nectoEvent.Key) ? id : null;
    }
}
