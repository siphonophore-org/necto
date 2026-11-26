using System;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using Jellyfin.Plugin.Necto.Services.Policies;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Policies;

/// <summary>
/// A Preferences template overwrites only the settings it manages, and leaves the rest as the member has them.
/// </summary>
public class PreferencesTemplateExtensionsTests
{
    /// <summary>
    /// Merge unmanaged setting leaves configuration untouched.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_UnmanagedSetting_LeavesConfigurationUntouched()
    {
        var config = new UserConfiguration { SubtitleMode = SubtitlePlaybackMode.None };
        var cfg = new PreferencesTemplate { ManageSubtitleMode = false, SubtitleMode = "Always" };

        cfg.ApplyTo(config);

        Assert.Equal(SubtitlePlaybackMode.None, config.SubtitleMode);
    }

    /// <summary>
    /// Merge managed subtitle mode overwrites configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedSubtitleMode_OverwritesConfiguration()
    {
        var config = new UserConfiguration { SubtitleMode = SubtitlePlaybackMode.Default };
        var cfg = new PreferencesTemplate { ManageSubtitleMode = true, SubtitleMode = "Smart" };

        cfg.ApplyTo(config);

        Assert.Equal(SubtitlePlaybackMode.Smart, config.SubtitleMode);
    }

    /// <summary>
    /// Merge unparsable subtitle mode leaves configuration untouched.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_UnparsableSubtitleMode_LeavesConfigurationUntouched()
    {
        var config = new UserConfiguration { SubtitleMode = SubtitlePlaybackMode.OnlyForced };
        var cfg = new PreferencesTemplate { ManageSubtitleMode = true, SubtitleMode = "NotAMode" };

        cfg.ApplyTo(config);

        Assert.Equal(SubtitlePlaybackMode.OnlyForced, config.SubtitleMode);
    }

    /// <summary>
    /// Merge managed language preferences overwrites configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedLanguagePreferences_OverwritesConfiguration()
    {
        var config = new UserConfiguration { AudioLanguagePreference = "eng", SubtitleLanguagePreference = "eng" };
        var cfg = new PreferencesTemplate
        {
            ManageAudioLanguagePreference = true,
            AudioLanguagePreference = "fra",
            ManageSubtitleLanguagePreference = true,
            SubtitleLanguagePreference = "deu"
        };

        cfg.ApplyTo(config);

        Assert.Equal("fra", config.AudioLanguagePreference);
        Assert.Equal("deu", config.SubtitleLanguagePreference);
    }

    /// <summary>
    /// Merge managed ordered views overwrites configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedOrderedViews_OverwritesConfiguration()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var config = new UserConfiguration { OrderedViews = new[] { Guid.NewGuid() } };
        var cfg = new PreferencesTemplate { ManageOrderedViews = true, OrderedViews = { a, b } };

        cfg.ApplyTo(config);

        Assert.Equal(new[] { a, b }, config.OrderedViews);
    }

    /// <summary>
    /// Merge managed excludes overwrites configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedExcludes_OverwritesConfiguration()
    {
        var hidden = Guid.NewGuid();
        var config = new UserConfiguration();
        var cfg = new PreferencesTemplate
        {
            ManageMyMediaExcludes = true,
            MyMediaExcludes = { hidden },
            ManageLatestItemsExcludes = true,
            LatestItemsExcludes = { hidden }
        };

        cfg.ApplyTo(config);

        Assert.Equal(new[] { hidden }, config.MyMediaExcludes);
        Assert.Equal(new[] { hidden }, config.LatestItemsExcludes);
    }

    /// <summary>
    /// Merge managed boolean toggles overwrite configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedBooleanToggles_OverwriteConfiguration()
    {
        var config = new UserConfiguration
        {
            PlayDefaultAudioTrack = true,
            EnableNextEpisodeAutoPlay = false,
            HidePlayedInLatest = false
        };
        var cfg = new PreferencesTemplate
        {
            ManagePlayDefaultAudioTrack = true,
            PlayDefaultAudioTrack = false,
            ManageEnableNextEpisodeAutoPlay = true,
            EnableNextEpisodeAutoPlay = true,
            ManageHidePlayedInLatest = true,
            HidePlayedInLatest = true
        };

        cfg.ApplyTo(config);

        Assert.False(config.PlayDefaultAudioTrack);
        Assert.True(config.EnableNextEpisodeAutoPlay);
        Assert.True(config.HidePlayedInLatest);
    }

    /// <summary>
    /// Merge managed display collections view overwrites configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_ManagedDisplayCollectionsView_OverwritesConfiguration()
    {
        var config = new UserConfiguration { DisplayCollectionsView = false };
        var cfg = new PreferencesTemplate { ManageDisplayCollectionsView = true, DisplayCollectionsView = true };

        cfg.ApplyTo(config);

        Assert.True(config.DisplayCollectionsView);
    }

    /// <summary>
    /// Merge unmanaged display collections view leaves configuration untouched.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void Merge_UnmanagedDisplayCollectionsView_LeavesConfigurationUntouched()
    {
        var config = new UserConfiguration { DisplayCollectionsView = true };
        var cfg = new PreferencesTemplate { ManageDisplayCollectionsView = false, DisplayCollectionsView = false };

        cfg.ApplyTo(config);

        Assert.True(config.DisplayCollectionsView);
    }

    /// <summary>
    /// Manages anything is true when display collections view managed.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void ManagesAnything_IsTrue_WhenDisplayCollectionsViewManaged()
    {
        Assert.True(new PreferencesTemplate { ManageDisplayCollectionsView = true }.ManagesAnything());
    }

    /// <summary>
    /// Manages anything is false for fresh configuration.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void ManagesAnything_IsFalse_ForFreshConfiguration()
    {
        Assert.False(new PreferencesTemplate().ManagesAnything());
    }

    /// <summary>
    /// Manages anything is true when any setting managed.
    /// True: the template writes only the values it manages, as User Management did.
    /// False: a member's own setting would be lost, or a managed one ignored.
    /// </summary>
    [Fact]
    public void ManagesAnything_IsTrue_WhenAnySettingManaged()
    {
        Assert.True(new PreferencesTemplate { ManageCastReceiverId = true }.ManagesAnything());
    }
}
