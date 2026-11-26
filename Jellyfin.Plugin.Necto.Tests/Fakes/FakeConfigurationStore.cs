using System;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Services.Configuration;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Holds a configuration in memory in place of the plugin's, and counts the saves a change asks for.
/// </summary>
public sealed class FakeConfigurationStore : IConfigurationStore
{
    private readonly object _lock = new();

    public FakeConfigurationStore(PluginConfiguration? config = null)
    {
        Configuration = config ?? new PluginConfiguration();
    }

    public PluginConfiguration Configuration { get; }

    public int Saves { get; private set; }

    public T Read<T>(Func<PluginConfiguration, T> read)
    {
        lock (_lock)
        {
            return read(Configuration);
        }
    }

    public void Mutate(Func<PluginConfiguration, bool> mutate)
    {
        lock (_lock)
        {
            if (mutate(Configuration))
            {
                Saves++;
            }
        }
    }
}
