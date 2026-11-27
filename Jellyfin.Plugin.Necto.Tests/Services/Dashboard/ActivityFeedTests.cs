using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Queries;
using Jellyfin.Plugin.Necto.Services.Dashboard;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Dashboard;

/// <summary>
/// The feed reads the activity log a page at a time and keeps Necto's entries, newest first.
/// </summary>
public class ActivityFeedTests
{
    private static readonly DateTime Start = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    private static ActivityLogEntry Entry(int minutesAgo, string type, string name = "Entry")
        => new(name, type, Guid.Empty) { Date = Start.AddMinutes(-minutesAgo), Severity = LogLevel.Information };

    private static (ActivityFeed Feed, List<ActivityLogQuery> Queries) Feed(List<ActivityLogEntry> log)
    {
        var queries = new List<ActivityLogQuery>();
        var manager = InterfaceStub.Of<IActivityManager>(("GetPagedResultAsync", args =>
        {
            var query = (ActivityLogQuery)args[0]!;
            queries.Add(query);
            var items = log.Skip(query.Skip ?? 0).Take(query.Limit ?? log.Count).ToList();
            return Task.FromResult(new QueryResult<ActivityLogEntry>(query.Skip, log.Count, items));
        }));
        return (new ActivityFeed(manager), queries);
    }

    /// <summary>
    /// Only Necto's entries come back, newest first, with their names and types.
    /// True: Recent activity shows what Necto's tools did.
    /// False: Jellyfin's own entries would fill the list.
    /// </summary>
    [Fact]
    public async Task RecentAsync_KeepsNectoEntriesNewestFirst()
    {
        var (feed, _) = Feed(new List<ActivityLogEntry>
        {
            Entry(1, "SessionStarted"),
            Entry(3, "Necto.GroupCreated", "Group created"),
            Entry(2, "Storage.DiskMissing", "Disk missing")
        });

        var items = await feed.RecentAsync(10);

        Assert.Equal(new[] { "Disk missing", "Group created" }, items.Select(i => i.Name));
        Assert.Equal("Storage.DiskMissing", items[0].Type);
    }

    /// <summary>
    /// The feed reads further pages until it has enough, stops at a short page, and never reads past its bound.
    /// True: a log busy with Jellyfin's own entries still shows Necto's, at a bounded cost.
    /// False: the Dashboard would read the whole log on every load.
    /// </summary>
    [Fact]
    public async Task RecentAsync_ReadsPagesUpToItsBound()
    {
        var log = Enumerable.Range(0, 150).Select(i => Entry(i, "SessionStarted")).Concat(new[] { Entry(200, "WireGuard", "Peer connected") }).ToList();
        var (feed, queries) = Feed(log);

        var items = await feed.RecentAsync(5);

        Assert.Equal("Peer connected", Assert.Single(items).Name);
        Assert.Equal(new int?[] { 0, 100 }, queries.Select(q => q.Skip));

        var busy = Enumerable.Range(0, 1000).Select(i => Entry(i, "SessionStarted")).ToList();
        var (bounded, read) = Feed(busy);
        Assert.Empty(await bounded.RecentAsync(5));
        Assert.Equal(5, read.Count);
    }

    /// <summary>
    /// The count is held between 1 and the most the Dashboard lists.
    /// True: a caller cannot ask for the whole log.
    /// False: a large limit would return thousands of entries.
    /// </summary>
    [Fact]
    public async Task RecentAsync_HoldsTheCount()
    {
        var (feed, _) = Feed(Enumerable.Range(0, 80).Select(i => Entry(i, "Necto.Thing")).ToList());

        Assert.Equal(ActivityFeed.MaxItems, (await feed.RecentAsync(500)).Count);
        Assert.Single(await feed.RecentAsync(0));
    }
}
