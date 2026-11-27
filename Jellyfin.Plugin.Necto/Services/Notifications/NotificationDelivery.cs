using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Groups;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Reads and moves a group's notification delivery. Only events about a member, those the catalog marks for users, go to
/// members, and each goes by the methods the group picks for it.
/// </summary>
public static class NotificationDelivery
{
    /// <summary>
    /// Lists the events a group sends to members' browsers, from a stored group's events when it has no list yet.
    /// </summary>
    /// <param name="permissions">The group's notification permissions.</param>
    /// <returns>The event keys.</returns>
    public static IReadOnlyList<string> Browser(NotificationPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.BrowserEventKeys ?? (permissions.BrowserNotifications ? Stored(permissions) : new List<string>());
    }

    /// <summary>
    /// Lists the events a group sends as Jellyfin messages, from a stored group's events when it has no list yet.
    /// </summary>
    /// <param name="permissions">The group's notification permissions.</param>
    /// <returns>The event keys.</returns>
    public static IReadOnlyList<string> Messages(NotificationPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.MessageEventKeys ?? (permissions.JellyfinMessages ? Stored(permissions) : new List<string>());
    }

    /// <summary>
    /// Moves a stored group's events into its delivery lists, keeping only events for users, and clears the stored ones.
    /// </summary>
    /// <param name="permissions">The group's notification permissions, changed in place.</param>
    public static void Complete(NotificationPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        permissions.BrowserEventKeys = Clean(Browser(permissions));
        permissions.MessageEventKeys = Clean(Messages(permissions));
        permissions.AllEvents = false;
        permissions.EventKeys = new List<string>();
        permissions.BrowserNotifications = true;
        permissions.JellyfinMessages = true;
    }

    /// <summary>
    /// Lists the events members may get, the ones about a member.
    /// </summary>
    /// <returns>The event keys, in catalog order.</returns>
    public static IReadOnlyList<string> ForUsers()
        => NectoEventCatalog.All.Where(e => e.ForUsers).Select(e => e.Key).ToList();

    private static List<string> Stored(NotificationPermissions permissions)
        => permissions.AllEvents ? ForUsers().ToList() : (permissions.EventKeys ?? new List<string>()).ToList();

    private static List<string> Clean(IEnumerable<string> keys)
    {
        var allowed = ForUsers();
        return keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
    }
}
