using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Groups;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Works out what a user may do in Requests and Issues, from the permissions their groups give and the windows set in
/// Settings.
/// </summary>
public static class PermissionRules
{
    /// <summary>
    /// Works out what a user may do, from the plugin's configuration.
    /// </summary>
    /// <param name="config">The configuration, with the groups and the Requests and Issues settings.</param>
    /// <param name="userId">The user.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <returns>The user's grant.</returns>
    public static RequestGrant For(PluginConfiguration config, Guid userId, bool isAdmin)
    {
        ArgumentNullException.ThrowIfNull(config);
        return From(PermissionService.Resolve(config, userId, isAdmin), config.Requests ?? new(), config.Issues?.LimitDays ?? 1);
    }

    /// <summary>
    /// Works out what a user may do from the loaded plugin. The Requests settings passed in are ignored, since the
    /// windows come from the same configuration as the groups.
    /// </summary>
    /// <param name="settings">The Requests settings the caller holds.</param>
    /// <param name="userId">The user.</param>
    /// <param name="isAdmin">Whether the user is an administrator.</param>
    /// <returns>The user's grant, which allows nothing but an administrator's when the plugin is not loaded.</returns>
    public static RequestGrant For(RequestSettings? settings, Guid userId, bool isAdmin)
        => Plugin.Instance?.ReadConfiguration(c => For(c, userId, isAdmin))
            ?? From(isAdmin ? PermissionMerger.Administrator(userId) : PermissionMerger.Nobody(userId), settings ?? new(), 1);

    /// <summary>
    /// Turns a user's merged permissions into their grant.
    /// </summary>
    /// <param name="permissions">The permissions from the user's groups.</param>
    /// <param name="settings">The Requests settings, for the quota windows.</param>
    /// <param name="issueDays">The issue limit's window in days.</param>
    /// <returns>The grant.</returns>
    public static RequestGrant From(EffectivePermissions permissions, RequestSettings settings, int issueDays)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(settings);
        var r = permissions.Requests ?? new RequestPermissions();
        var i = permissions.Issues ?? new IssuePermissions();
        var can = r.MayRequest;
        return new RequestGrant(
            can,
            can && r.Movies,
            can && r.Series,
            can && r.AutoApproveMovies,
            can && r.AutoApproveSeries,
            can && r.MaySeeOthers,
            can && r.MaySeeRequesters,
            can && r.Unreleased,
            Math.Max(0, r.MaxOpenRequests),
            Math.Max(0, r.MovieQuota),
            Math.Max(1, settings.MovieQuotaDays),
            Math.Max(0, r.SeriesQuota),
            Math.Max(1, settings.SeriesQuotaDays),
            Math.Max(0, r.MaxSeasons),
            (r.MovieProfiles ?? new()).ToList(),
            (r.SeriesProfiles ?? new()).ToList(),
            i.MayReport,
            Math.Max(0, i.Limit),
            Math.Max(1, issueDays),
            r.MayManage || permissions.IsAdministrator,
            i.MayManage || permissions.IsAdministrator);
    }

    /// <summary>Gets the longest quota window any group holds its members to, so cleanup keeps the requests quotas still count.</summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The days, or 0 when no group has a quota.</returns>
    public static int LongestQuotaDays(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var groups = (config.Groups ?? new()).Append(config.Everyone)
            .Where(g => g?.Permissions?.Requests is { MayRequest: true })
            .Select(g => g!.Permissions.Requests)
            .ToList();
        var settings = config.Requests ?? new();
        var movie = groups.Any(r => r.MovieQuota > 0) ? Math.Max(1, settings.MovieQuotaDays) : 0;
        var show = groups.Any(r => r.SeriesQuota > 0) ? Math.Max(1, settings.SeriesQuotaDays) : 0;
        return Math.Max(movie, show);
    }
}
