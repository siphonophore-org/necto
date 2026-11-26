using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Policies;

namespace Jellyfin.Plugin.Necto.Services.Policies;

/// <summary>
/// Fills every part of a template a post left out, and drops blanks from its lists.
/// </summary>
public static class PolicyTemplateNormalizer
{
    /// <summary>
    /// Completes a template in place.
    /// </summary>
    /// <param name="template">The template.</param>
    public static void Complete(PolicyTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var a = template.Access ??= new AccessTemplate();
        a.SyncPlayAccess ??= "CreateAndJoinGroups";
        a.EnableContentDeletionFromFolders = Strings(a.EnableContentDeletionFromFolders);
        a.EnabledFolders = (a.EnabledFolders ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        a.EnabledChannels = (a.EnabledChannels ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
        a.EnabledDevices = Strings(a.EnabledDevices);
        a.BlockUnratedItems = Strings(a.BlockUnratedItems);
        a.AllowedTags = Strings(a.AllowedTags);
        a.BlockedTags = Strings(a.BlockedTags);
        a.AccessSchedules = (a.AccessSchedules ?? new List<AccessScheduleEntry>()).Where(s => s is not null).ToList();

        var p = template.Preferences ??= new PreferencesTemplate();
        p.SubtitleMode ??= "Default";
        p.SubtitleLanguagePreference ??= string.Empty;
        p.AudioLanguagePreference ??= string.Empty;
        p.CastReceiverId ??= string.Empty;
        p.OrderedViews = Ids(p.OrderedViews);
        p.MyMediaExcludes = Ids(p.MyMediaExcludes);
        p.LatestItemsExcludes = Ids(p.LatestItemsExcludes);
        p.GroupedFolders = Ids(p.GroupedFolders);
        p.HomeSections = (p.HomeSections ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
    }

    private static List<string> Strings(List<string>? values)
        => (values ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).ToList();

    private static List<Guid> Ids(List<Guid>? values) => (values ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
}
