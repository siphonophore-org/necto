using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// The events Invites sends. Each goes to the administrators' channels that pick it, and to the member who made the
/// invite when their groups give them the event.
/// </summary>
public static class InvitesEvents
{
    /// <summary>The key of an account made with an invite.</summary>
    public const string Used = "invites.used";

    /// <summary>The key of an invite that made its last account.</summary>
    public const string UsedUp = "invites.usedUp";

    /// <summary>The key of an invite that locked itself after too many wrong PINs.</summary>
    public const string Locked = "invites.locked";

    /// <summary>The key of invites turned off when their day came.</summary>
    public const string Expired = "invites.expired";

    /// <summary>The key of an invite a member made.</summary>
    public const string Created = "invites.created";

    /// <summary>Gets the events Invites sends.</summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Used, TranslationKeys.InvitesEventUsed, ForUsers: true),
        new NectoEventInfo(UsedUp, TranslationKeys.InvitesEventUsedUp, ForUsers: true),
        new NectoEventInfo(Locked, TranslationKeys.InvitesEventLocked, ForUsers: true),
        new NectoEventInfo(Expired, TranslationKeys.InvitesEventExpired),
        new NectoEventInfo(Created, TranslationKeys.InvitesEventCreated)
    };
}
