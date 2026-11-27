using Jellyfin.Plugin.Necto.Configuration.Scripts;

namespace Jellyfin.Plugin.Necto.Services.Scripts;

/// <summary>
/// Reads the Scripts settings of the loaded plugin. A save replaces the settings whole, so a caller holding them keeps
/// one consistent set, and a new set is a new object.
/// </summary>
public static class ScriptConfig
{
    /// <summary>
    /// Gets the Scripts settings in effect.
    /// </summary>
    /// <returns>The settings, or null while the plugin has none, such as in tests.</returns>
    public static ScriptSettings? Current() => Plugin.Instance?.ReadConfiguration(c => c?.Scripts);

    /// <summary>
    /// Gets a value indicating whether Scripts is turned off in the loaded plugin's settings, so nothing runs.
    /// </summary>
    /// <returns><c>true</c> while Scripts is off.</returns>
    public static bool IsOff() => Current() is { Enabled: false };
}
