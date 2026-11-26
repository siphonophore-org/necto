namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// What a test or a send to one channel did, in a sentence a person reads.
/// </summary>
/// <param name="Ok"><c>true</c> when it reached the channel, or was held for an app to come online.</param>
/// <param name="Message">What happened.</param>
public sealed record NotificationResult(bool Ok, string Message);
