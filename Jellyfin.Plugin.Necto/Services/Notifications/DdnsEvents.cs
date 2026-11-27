using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// Every event the DDNS tool sends to Notifications, so Notifications can list them.
/// </summary>
public static class DdnsEvents
{
    /// <summary>A record could not be pushed to its provider, so its name may point at an old address.</summary>
    public const string Failed = "ddns.failed";

    /// <summary>A record failed so often that it is paused for a while.</summary>
    public const string Paused = "ddns.paused";

    /// <summary>A record that was failing was pushed again.</summary>
    public const string Recovered = "ddns.recovered";

    /// <summary>The public IP address could not be found for several runs in a row.</summary>
    public const string DetectionFailed = "ddns.detection_failed";

    /// <summary>
    /// Gets every event, in the order Notifications lists them.
    /// </summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Failed, TranslationKeys.DdnsEventFailed),
        new NectoEventInfo(Paused, TranslationKeys.DdnsEventPaused),
        new NectoEventInfo(Recovered, TranslationKeys.DdnsEventRecovered),
        new NectoEventInfo(DetectionFailed, TranslationKeys.DdnsEventDetectionFailed)
    };
}
