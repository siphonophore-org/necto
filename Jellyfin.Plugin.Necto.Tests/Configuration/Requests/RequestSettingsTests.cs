using System.IO;
using System.Xml.Serialization;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Configuration.Requests;

/// <summary>
/// The Requests settings start from their documented defaults and keep what is chosen through the settings file.
/// </summary>
public class RequestSettingsTests
{
    /// <summary>
    /// Refreshing on arrival defaults to the item's folder, and another choice survives the settings file.
    /// True: an arrival scans only its folder unless Settings says otherwise.
    /// False: arrivals would scan whole libraries, or the choice would be lost.
    /// </summary>
    [Fact]
    public void RefreshOnAvailable_Default_IsTheItemFolder()
    {
        var config = new PluginConfiguration();
        config.Requests.RefreshOnAvailable = LibraryRefresh.Disabled;
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var buffer = new MemoryStream();

        serializer.Serialize(buffer, config);
        buffer.Position = 0;

        Assert.Equal(LibraryRefresh.ItemFolder, new RequestSettings().RefreshOnAvailable);
        Assert.Equal(LibraryRefresh.Disabled, ((PluginConfiguration)serializer.Deserialize(buffer)!).Requests.RefreshOnAvailable);
    }

    /// <summary>
    /// The quota windows default to seven days each.
    /// True: a quota set on a group counts over a week until Settings says otherwise.
    /// False: a quota would count over no window.
    /// </summary>
    [Fact]
    public void QuotaDays_Default_IsAWeek()
    {
        var settings = new RequestSettings();

        Assert.Equal(7, settings.MovieQuotaDays);
        Assert.Equal(7, settings.SeriesQuotaDays);
    }
}
