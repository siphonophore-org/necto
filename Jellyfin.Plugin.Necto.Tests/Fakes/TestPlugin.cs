using System;
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
        var (serializer, _) = InterfaceStub.Create<IXmlSerializer>();
        return new Plugin(Paths(), serializer);
    }

    /// <summary>
    /// Creates the plugin with fresh settings held in memory, which a save leaves where they are. The plugin becomes
    /// the loaded one that <see cref="Plugin.Instance"/> names.
    /// </summary>
    /// <returns>The plugin.</returns>
    public static Plugin CreateWithSettings()
    {
        var (serializer, stub) = InterfaceStub.Create<IXmlSerializer>();
        stub.Answer("DeserializeFromFile", args => Activator.CreateInstance((Type)args[0]!));
        stub.Answer("DeserializeFromStream", args => Activator.CreateInstance((Type)args[0]!));
        stub.Answer("DeserializeFromBytes", args => Activator.CreateInstance((Type)args[0]!));
        return new Plugin(Paths(), serializer);
    }

    private static IApplicationPaths Paths()
    {
        var (paths, pathsStub) = InterfaceStub.Create<IApplicationPaths>();
        var folder = Path.Combine(Path.GetTempPath(), "necto-tests");
        pathsStub.Answer("get_PluginsPath", _ => folder);
        pathsStub.Answer("get_PluginConfigurationsPath", _ => folder);
        return paths;
    }
}
