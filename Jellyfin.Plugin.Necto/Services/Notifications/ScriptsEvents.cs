using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The events Scripts sends to Notifications, in one place so Notifications can list them.
/// </summary>
public static class ScriptsEvents
{
    /// <summary>A script run ended with a failed step.</summary>
    public const string Failed = "scripts.failed";

    /// <summary>
    /// Gets every Scripts event.
    /// </summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Failed, TranslationKeys.ScriptsActivityNameRunFailedOne)
    };
}
