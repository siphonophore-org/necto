using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Notifications;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Api.Notifications;

/// <summary>
/// One channel as the Notifications page lists it.
/// </summary>
/// <param name="Id">The channel's id, which its test names.</param>
/// <param name="Kind">Browsers, Apps, or the administrators' channel's kind, such as Discord.</param>
/// <param name="Name">The name an added channel was given, or empty for one Necto keeps.</param>
/// <param name="Enabled">Whether the channel is on.</param>
/// <param name="Ready">Whether the channel has what it needs to send, such as an address.</param>
/// <param name="AllEvents">Whether every event goes there. The user channels take what each user's groups give.</param>
/// <param name="EventCount">How many events it picks.</param>
public sealed record NotificationChannelView(string Id, string Kind, string Name, bool Enabled, bool Ready, bool AllEvents, int EventCount)
{
    /// <summary>
    /// Lists the channels of the settings, the user channels first.
    /// </summary>
    /// <param name="settings">The normalized settings.</param>
    /// <returns>The channels.</returns>
    public static IReadOnlyList<NotificationChannelView> List(NotificationSettings settings)
    {
        System.ArgumentNullException.ThrowIfNull(settings);
        var list = new List<NotificationChannelView>
        {
            new(NotificationRouter.BrowsersId, "Browsers", string.Empty, settings.BrowserNotifications, true, false, 0),
            new(NotificationRouter.AppsId, "Apps", string.Empty, settings.JellyfinMessages, true, false, 0)
        };
        list.AddRange(settings.Channels.Select(c => new NotificationChannelView(c.Id, c.Kind.ToString(), c.Name, c.Enabled, IsReady(c, settings), c.AllEvents, c.EventKeys.Count)));
        return list;
    }

    private static bool IsReady(NotificationChannel channel, NotificationSettings settings) => channel.Kind switch
    {
        NotificationChannelKind.Discord or NotificationChannelKind.Webhook => SecretProtector.HasSecret(channel.Url),
        NotificationChannelKind.Email => settings.Smtp.IsSet && NotificationNormalizer.Addresses(channel.Recipients).Count > 0,
        _ => true
    };
}
