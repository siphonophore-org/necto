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
    /// Gets every Scripts event key, with the key of its name in the plugin's strings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [Failed] = TranslationKeys.ScriptsActivityNameRunFailedOne
    };
}
