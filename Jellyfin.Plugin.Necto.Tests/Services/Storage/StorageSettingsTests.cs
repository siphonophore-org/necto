using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// The Storage settings come from the Storage section of the plugin configuration, with the defaults when it has none.
/// </summary>
public class StorageSettingsTests
{
    /// <summary>
    /// The settings read the Storage section as saved.
    /// True: a change in Settings reaches the next check.
    /// False: the checks would keep reading the defaults.
    /// </summary>
    [Fact]
    public void Current_ReadsTheStorageSection()
    {
        var config = new PluginConfiguration();
        config.Storage.Remount = false;
        var settings = new StorageSettings(new FakeConfigurationStore(config));

        Assert.False(settings.Current.Remount);
    }

    /// <summary>
    /// A configuration with no Storage section gives the defaults.
    /// True: a config saved before Storage existed still checks the disks.
    /// False: every check would throw.
    /// </summary>
    [Fact]
    public void Current_WithNoSection_GivesTheDefaults()
    {
        var config = new PluginConfiguration { Storage = null! };
        var settings = new StorageSettings(new FakeConfigurationStore(config));

        Assert.Equal(90, settings.Current.WarningStop.Value);
        Assert.Equal(ScanGuardMode.Affected, settings.Current.ScanGuard);
    }

    /// <summary>
    /// Saving the configuration keeps the Error alert past the Warning alert.
    /// True: an API client that saves alerts out of order is corrected.
    /// False: a disk could be full before it is low.
    /// </summary>
    [Fact]
    public void Saving_KeepsTheAlertsInOrder()
    {
        var plugin = TestPlugin.Create();
        var config = new PluginConfiguration();
        config.Storage.WarningStop.Value = 95;
        config.Storage.ErrorStop.Value = 90;

        plugin.UpdateConfiguration(config);

        Assert.True(plugin.Configuration.Storage.ErrorStop.Value > plugin.Configuration.Storage.WarningStop.Value);
    }
}
