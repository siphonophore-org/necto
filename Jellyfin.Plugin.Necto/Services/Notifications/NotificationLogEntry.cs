using System;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One send of an event to one channel, as the send log in Troubleshooting lists it.
/// </summary>
/// <param name="Time">When it was sent, in UTC.</param>
/// <param name="EventKey">The event's key.</param>
/// <param name="Title">The event's title.</param>
/// <param name="Channel">The channel's name.</param>
/// <param name="Ok"><c>true</c> when it reached the channel, or was held for an app to come online.</param>
/// <param name="Message">What happened, in a sentence.</param>
/// <param name="Test"><c>true</c> for a test from the Notifications page.</param>
public sealed record NotificationLogEntry(DateTime Time, string EventKey, string Title, string Channel, bool Ok, string Message, bool Test = false);
