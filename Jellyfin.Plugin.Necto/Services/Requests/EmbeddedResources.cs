using System.IO;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// The request page's files built into the plugin, read from its own assembly.
/// </summary>
internal static class EmbeddedResources
{
    /// <summary>
    /// Reads one of the files under Web, such as the request page.
    /// </summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>Its text, or a short notice when it is not there.</returns>
    public static string Read(string fileName)
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web." + fileName);
        if (stream is null)
        {
            return "<p>The request page is unavailable.</p>";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Opens one of the request page's app icons.
    /// </summary>
    /// <param name="fileName">The icon's file name, such as <c>requests-192.png</c>.</param>
    /// <returns>Its stream, or <c>null</c> when it is not there.</returns>
    public static Stream? OpenIcon(string fileName)
        => typeof(Plugin).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.necto_requests_icon_" + fileName);
}
