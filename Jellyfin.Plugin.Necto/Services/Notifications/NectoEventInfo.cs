using System;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One event a tool may send, as Notifications lists it. A tool lists its events in a static class named
/// <c>&lt;Tool&gt;Events</c> with a <c>public static IReadOnlyList&lt;NectoEventInfo&gt; All</c>, which
/// <see cref="NectoEventCatalog"/> finds.
/// </summary>
/// <param name="Key">The event's key, such as <c>invites.used</c>. The part before the dot is the tool's key.</param>
/// <param name="NameKey">The translation key of the event's name, such as <c>InvitesEventUsed</c>.</param>
/// <param name="ForUsers"><c>true</c> when the event can go to the user it is about, as their groups allow. Every event can go to the administrators' channels.</param>
public sealed record NectoEventInfo(string Key, string NameKey, bool ForUsers = false)
{
    /// <summary>Gets the tool's key, the part of <see cref="Key"/> before the dot.</summary>
    public string Tool => Key.Contains('.', StringComparison.Ordinal) ? Key[..Key.IndexOf('.', StringComparison.Ordinal)] : Key;
}
