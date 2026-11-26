using System;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Configuration;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Gives the Storage settings as saved, so a test can give its own.
/// </summary>
public class StorageSettings
{
    private readonly IConfigurationStore? _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageSettings"/> class.
    /// </summary>
    /// <param name="store">Reads the plugin configuration.</param>
    public StorageSettings(IConfigurationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageSettings"/> class with no store, for a test that gives its own.
    /// </summary>
    protected StorageSettings()
    {
    }

    /// <summary>Gets the settings as saved.</summary>
    public virtual StorageConfiguration Current => _store?.Read(config => config.Storage ?? new StorageConfiguration()) ?? new StorageConfiguration();
}
