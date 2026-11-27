namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Who an event goes to. A tool splits an event in two when its users read other words than its channels.
/// </summary>
public enum NectoEventAudience
{
    /// <summary>The users it is about or the groups that pick it, and every channel that picks it.</summary>
    All,

    /// <summary>Only the users it is about, never a channel.</summary>
    Users,

    /// <summary>Only the channels that pick it, never a user.</summary>
    Channels
}
