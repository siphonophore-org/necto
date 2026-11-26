using System;
using Jellyfin.Plugin.Necto.Configuration;

namespace Jellyfin.Plugin.Necto.Services.Configuration;

/// <summary>
/// Reads and changes the plugin configuration under one lock, so a read never meets a change halfway.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>
    /// Reads a value from the configuration.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="read">Picks the value out of the configuration.</param>
    /// <returns>The value.</returns>
    T Read<T>(Func<PluginConfiguration, T> read);

    /// <summary>
    /// Changes the configuration and saves it when the change asks.
    /// </summary>
    /// <param name="mutate">Changes the configuration in place, and returns <c>true</c> to save it.</param>
    void Mutate(Func<PluginConfiguration, bool> mutate);
}
