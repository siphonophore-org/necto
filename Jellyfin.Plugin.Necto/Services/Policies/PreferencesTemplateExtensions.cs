using System;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Necto.Configuration.Policies;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Writes a Preferences template onto a Jellyfin user's settings. Only the values the template manages overwrite the
/// user's own, so an unmanaged setting stays as the user has it.
/// </summary>
public static class PreferencesTemplateExtensions
{
    /// <summary>
    /// Writes the managed values of a template onto a user's settings in place.
    /// </summary>
    /// <param name="c">The template.</param>
    /// <param name="config">The user's settings to update.</param>
    public static void ApplyTo(this PreferencesTemplate c, UserConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(config);

        if (c.ManageSubtitleMode
            && Enum.TryParse<SubtitlePlaybackMode>(c.SubtitleMode, out var mode))
        {
            config.SubtitleMode = mode;
        }

        if (c.ManageSubtitleLanguagePreference)
        {
            config.SubtitleLanguagePreference = c.SubtitleLanguagePreference ?? string.Empty;
        }

        if (c.ManageRememberSubtitleSelections)
        {
            config.RememberSubtitleSelections = c.RememberSubtitleSelections;
        }

        if (c.ManageAudioLanguagePreference)
        {
            config.AudioLanguagePreference = c.AudioLanguagePreference ?? string.Empty;
        }

        if (c.ManagePlayDefaultAudioTrack)
        {
            config.PlayDefaultAudioTrack = c.PlayDefaultAudioTrack;
        }

        if (c.ManageRememberAudioSelections)
        {
            config.RememberAudioSelections = c.RememberAudioSelections;
        }

        if (c.ManageEnableNextEpisodeAutoPlay)
        {
            config.EnableNextEpisodeAutoPlay = c.EnableNextEpisodeAutoPlay;
        }

        if (c.ManageCastReceiverId)
        {
            config.CastReceiverId = c.CastReceiverId ?? string.Empty;
        }

        if (c.ManageDisplayMissingEpisodes)
        {
            config.DisplayMissingEpisodes = c.DisplayMissingEpisodes;
        }

        if (c.ManageDisplayCollectionsView)
        {
            config.DisplayCollectionsView = c.DisplayCollectionsView;
        }

        if (c.ManageHidePlayedInLatest)
        {
            config.HidePlayedInLatest = c.HidePlayedInLatest;
        }

        if (c.ManageOrderedViews)
        {
            config.OrderedViews = c.OrderedViews.ToArray();
        }

        if (c.ManageMyMediaExcludes)
        {
            config.MyMediaExcludes = c.MyMediaExcludes.ToArray();
        }

        if (c.ManageLatestItemsExcludes)
        {
            config.LatestItemsExcludes = c.LatestItemsExcludes.ToArray();
        }

        if (c.ManageGroupedFolders)
        {
            config.GroupedFolders = c.GroupedFolders.ToArray();
        }
    }

    /// <summary>
    /// Works out whether a template manages any setting.
    /// </summary>
    /// <param name="c">The template.</param>
    /// <returns><c>true</c> when at least one setting is managed.</returns>
    public static bool ManagesAnything(this PreferencesTemplate c)
        => c is not null && (c.ManageSubtitleMode
            || c.ManageSubtitleLanguagePreference
            || c.ManageRememberSubtitleSelections
            || c.ManageAudioLanguagePreference
            || c.ManagePlayDefaultAudioTrack
            || c.ManageRememberAudioSelections
            || c.ManageEnableNextEpisodeAutoPlay
            || c.ManageCastReceiverId
            || c.ManageDisplayMissingEpisodes
            || c.ManageDisplayCollectionsView
            || c.ManageHidePlayedInLatest
            || c.ManageOrderedViews
            || c.ManageMyMediaExcludes
            || c.ManageLatestItemsExcludes
            || c.ManageGroupedFolders);
}
