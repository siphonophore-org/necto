using System;
using Jellyfin.Plugin.Necto.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Configuration;

/// <summary>
/// Reads and changes the configuration the <see cref="Plugin"/> instance holds, under its configuration lock.
/// </summary>
[PluginService(ServiceLifetime.Singleton, ServiceType = typeof(IConfigurationStore))]
public sealed class ConfigurationStore : IConfigurationStore
{
    /// <inheritdoc />
    public T Read<T>(Func<PluginConfiguration, T> read) => PluginInstance().ReadConfiguration(read);

    /// <inheritdoc />
    public void Mutate(Func<PluginConfiguration, bool> mutate) => PluginInstance().MutateConfiguration(mutate);

    private static Plugin PluginInstance() => Plugin.Instance ?? throw new InvalidOperationException("The plugin is not loaded.");
}
