using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Queries;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// Reads Necto's latest entries from Jellyfin's activity log for the Dashboard.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class ActivityFeed
{
    /// <summary>The most entries the Dashboard lists.</summary>
    public const int MaxItems = 50;

    // The log holds every plugin's entries and Jellyfin's own, so it is read a page at a time, newest first, up to a bound.
    private const int PageSize = 100;
    private const int MaxPages = 5;

    private readonly IActivityManager _activity;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityFeed"/> class.
    /// </summary>
    /// <param name="activity">Jellyfin's activity log.</param>
    public ActivityFeed(IActivityManager activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    /// <summary>
    /// Gives Necto's newest entries, from the latest few hundred entries of the log.
    /// </summary>
    /// <param name="count">How many to give, from 1 to <see cref="MaxItems"/>.</param>
    /// <returns>The entries, newest first.</returns>
    public async Task<IReadOnlyList<ActivityItem>> RecentAsync(int count)
    {
        count = Math.Clamp(count, 1, MaxItems);
        var found = new List<ActivityItem>();
        for (var page = 0; page < MaxPages && found.Count < count; page++)
        {
            var result = await _activity.GetPagedResultAsync(new ActivityLogQuery { Skip = page * PageSize, Limit = PageSize }).ConfigureAwait(false);
            var items = result?.Items ?? Array.Empty<ActivityLogEntry>();
            found.AddRange(items.Where(e => NectoActivity.Owns(e.Type))
                .Select(e => new ActivityItem(e.Date, e.Name ?? string.Empty, e.Overview, e.Type ?? string.Empty, e.Severity, e.UserId)));
            if (items.Count < PageSize)
            {
                break;
            }
        }

        return found.OrderByDescending(e => e.Date).Take(count).ToList();
    }
}
