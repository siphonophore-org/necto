using System;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests;

/// <summary>
/// The Requests settings are read and changed through the loaded plugin's configuration, each section made when missing.
/// It loads the plugin, so it runs on its own.
/// </summary>
[Collection(ServicesCollection.Name)]
public class RequestConfigTests
{
    /// <summary>
    /// Reads and changes reach the loaded plugin's Requests, Issues, and whole configuration.
    /// True: every Requests service sees the settings the plugin holds, and a change lands there.
    /// False: a service would read stale or separate settings.
    /// </summary>
    [Fact]
    public void Instance_LoadedPlugin_ReadsAndChangesItsSettings()
    {
        var plugin = TestPlugin.CreateWithSettings();
        var config = RequestConfig.Instance!;

        config.MutateConfiguration(r => { r.RetentionDays = 12; return false; });
        config.MutateAll(c => { c.Issues.LimitDays = 4; return false; });

        Assert.Equal(12, plugin.Configuration.Requests.RetentionDays);
        Assert.Equal(12, config.ReadConfiguration(r => r.RetentionDays));
        Assert.Equal(4, config.ReadIssues(i => i.LimitDays));
        Assert.Same(plugin.Configuration.Everyone, config.ReadAll(c => c.Everyone));
        Assert.Same(RequestSecrets.Current, config.Secrets);
    }

    /// <summary>
    /// A missing Requests or Issues section is made when read.
    /// True: settings saved before a section existed still read.
    /// False: a service would fail on a null section.
    /// </summary>
    [Fact]
    public void ReadConfiguration_MissingSection_IsMade()
    {
        var plugin = TestPlugin.CreateWithSettings();
        plugin.Configuration.Requests = null!;
        plugin.Configuration.Issues = null!;

        Assert.Equal(7, RequestConfig.Instance!.ReadConfiguration(r => r.MovieQuotaDays));
        Assert.Equal(90, RequestConfig.Instance!.ReadIssues(i => i.RetentionDays));
        Assert.NotNull(plugin.Configuration.Requests);
        Assert.NotNull(plugin.Configuration.Issues);
    }

    /// <summary>
    /// Null delegates are refused.
    /// True: a caller's mistake fails at once with the argument named.
    /// False: a null read would fail deep inside the lock.
    /// </summary>
    [Fact]
    public void ReadConfiguration_Null_Throws()
    {
        TestPlugin.CreateWithSettings();
        var config = RequestConfig.Instance!;

        Assert.Throws<ArgumentNullException>(() => config.ReadConfiguration<int>(null!));
        Assert.Throws<ArgumentNullException>(() => config.ReadIssues<int>(null!));
        Assert.Throws<ArgumentNullException>(() => config.ReadAll<int>(null!));
        Assert.Throws<ArgumentNullException>(() => config.MutateConfiguration(null!));
        Assert.Throws<ArgumentNullException>(() => config.MutateAll(null!));
    }
}
