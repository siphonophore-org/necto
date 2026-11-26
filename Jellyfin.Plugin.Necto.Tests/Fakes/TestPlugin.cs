using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds the plugin over stand-ins for Jellyfin's paths and serializer, with the plugins folder in the temp folder.
/// </summary>
public static class TestPlugin
{
    /// <summary>
    /// Creates the plugin the way Jellyfin does, without reading or writing its settings.
    /// </summary>
    /// <returns>The plugin.</returns>
    public static Plugin Create()
    {
        var (paths, pathsStub) = InterfaceStub.Create<IApplicationPaths>();
        var folder = Path.Combine(Path.GetTempPath(), "necto-tests");
        pathsStub.Answer("get_PluginsPath", _ => folder);
        pathsStub.Answer("get_PluginConfigurationsPath", _ => folder);
        var (serializer, _) = InterfaceStub.Create<IXmlSerializer>();
        return new Plugin(paths, serializer);
    }
}
