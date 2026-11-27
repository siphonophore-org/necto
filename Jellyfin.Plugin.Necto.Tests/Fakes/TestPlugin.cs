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
        var (serializer, stub) = InterfaceStub.Create<IXmlSerializer>();
        stub.Answer("DeserializeFromFile", args => Fresh((Type)args[0]!));
        stub.Answer("DeserializeFromStream", args => Fresh((Type)args[0]!));
        stub.Answer("DeserializeFromBytes", args => Fresh((Type)args[0]!));
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
        stub.Answer("DeserializeFromFile", args => Fresh((Type)args[0]!));
        stub.Answer("DeserializeFromStream", args => Fresh((Type)args[0]!));
        stub.Answer("DeserializeFromBytes", args => Fresh((Type)args[0]!));
        return new Plugin(Paths(), serializer);
    }

    /// <summary>
    /// Turns on the tools whose tests expect them to work, which a fresh install has off: Requests, Invites, DDNS,
    /// Scripts, Pages, and Storage. Issues, Proxy, and WireGuard stay off, as their tests turn them on themselves.
    /// </summary>
    /// <param name="config">The settings.</param>
    /// <returns>The same settings.</returns>
    public static Jellyfin.Plugin.Necto.Configuration.PluginConfiguration WithToolsOn(Jellyfin.Plugin.Necto.Configuration.PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Requests is { } requests)
        {
            requests.Enabled = true;
        }

        if (config.InviteSettings is { } invites)
        {
            invites.Enabled = true;
        }

        if (config.Ddns is { } ddns)
        {
            ddns.Enabled = true;
        }

        if (config.Scripts is { } scripts)
        {
            scripts.Enabled = true;
        }

        if (config.CustomPages is { } pages)
        {
            pages.Enabled = true;
        }

        if (config.Storage is { } storage)
        {
            storage.Enabled = true;
        }

        return config;
    }

    // A fresh copy of the settings, with the tools the tests use on.
    private static object? Fresh(Type type)
    {
        var made = Activator.CreateInstance(type);
        return made is Jellyfin.Plugin.Necto.Configuration.PluginConfiguration config ? WithToolsOn(config) : made;
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
