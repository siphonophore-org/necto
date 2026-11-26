namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// One event as the pages list it.
/// </summary>
/// <param name="Key">The event's key.</param>
/// <param name="Name">The event's name in the viewer's language.</param>
/// <param name="Tool">The key of the tool that sends it.</param>
/// <param name="ForUsers">Whether it can go to the user it is about.</param>
public sealed record NotificationEventView(string Key, string Name, string Tool, bool ForUsers);
