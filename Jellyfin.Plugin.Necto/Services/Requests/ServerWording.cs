using Siphonophore.Stolon;
using MediaBrowser.Controller.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// The plugin's strings for text the server writes for everyone, such as updates, activity log entries and the
/// scheduled tasks' names, in the server's display language. Read each time, so a change in the dashboard applies
/// at once.
/// </summary>
internal static class ServerWording
{
    private static IServerConfigurationManager? _config;

    /// <summary>Gets the server's display language, or US English until the plugin's services start.</summary>
    internal static string Language => _config?.Configuration is null ? StolonTranslations.FallbackLanguage : StolonTranslations.ServerLanguage(_config);

    /// <summary>Points the wording at the server's settings, as the plugin's services start.</summary>
    /// <param name="config">The server configuration.</param>
    internal static void Use(IServerConfigurationManager config) => _config = config;

    /// <summary>Gets a string in the server's display language.</summary>
    /// <param name="key">The string's key, from <see cref="TranslationKeys"/>.</param>
    /// <returns>The string.</returns>
    internal static string Get(string key) => StolonTranslations.Get(key, Language);

    /// <summary>Gets a string in the server's display language with its placeholders filled.</summary>
    /// <param name="key">The string's key, from <see cref="TranslationKeys"/>.</param>
    /// <param name="args">The values for {0}, {1} and on.</param>
    /// <returns>The filled string.</returns>
    internal static string Format(string key, params object?[] args) => StolonTranslations.Format(key, Language, args);
}
