using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The events Notifications sends itself.
/// </summary>
public static class NotificationsEvents
{
    /// <summary>The key of the test the Notifications page sends.</summary>
    public const string Test = "notifications.test";

    /// <summary>Gets the events Notifications sends.</summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Test, TranslationKeys.NotificationsEventTest, ForUsers: true)
    };
}
