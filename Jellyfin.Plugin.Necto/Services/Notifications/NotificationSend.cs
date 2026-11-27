using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// One send <see cref="NotificationRouter"/> plans for an event.
/// </summary>
/// <param name="Target">Where it goes.</param>
/// <param name="ChannelId">The administrators' channel it goes through, or the user channel's own id.</param>
/// <param name="Recipients">The Jellyfin users it goes to, for the user channels and Administrators. Empty for the rest.</param>
public sealed record NotificationSend(NotificationTarget Target, string ChannelId, IReadOnlyList<Guid> Recipients);
