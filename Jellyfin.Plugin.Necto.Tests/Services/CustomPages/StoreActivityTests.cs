using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.CustomPages;

/// <summary>
/// Store entries go to the activity log and to Notifications, summed per burst, with a cleared store sent at once.
/// </summary>
public sealed class StoreActivityTests : IDisposable
{
    private readonly FakeActivity _log = new();
    private readonly TestServices.RecordingNotifier _notifier = new();
    private readonly StoreActivity _activity;

    public StoreActivityTests()
    {
        _activity = new StoreActivity(_log.Logger, () => null, _notifier, TimeSpan.FromHours(1));
    }

    public void Dispose() => _activity.Dispose();

    /// <summary>
    /// A burst of added records becomes one event with the summed title, the stores, and the count.
    /// True: Notifications gets one message per burst, like the activity log.
    /// False: a busy form floods every channel.
    /// </summary>
    [Fact]
    public void RecordAdded_SendsOneSummedEventPerBurst()
    {
        _activity.RecordAdded("queue", null);
        _activity.RecordAdded("queue", null);
        _activity.RecordAdded("queue", null);

        Assert.Empty(_notifier.Events);
        _activity.Flush();

        var sent = Assert.Single(_notifier.Events);
        Assert.Equal(CustomPageEvents.RecordsAdded, sent.Key);
        Assert.Equal("Records added", sent.Title);
        Assert.Equal("Added 3 records to queue", sent.Body);
        Assert.Equal("3", sent.Data["count"]);
        Assert.Equal("queue", sent.Data["stores"]);
        Assert.Equal("Added 3 records to queue", Assert.Single(_log.Entries).ShortOverview);
    }

    /// <summary>
    /// A removed record names its label in the event, as in the activity log.
    /// True: an administrator reads which record went.
    /// False: the event carries only a count.
    /// </summary>
    [Fact]
    public void RecordRemoved_SendsTheLabel()
    {
        _activity.RecordRemoved("queue", "Big Buck Bunny");
        _activity.Flush();

        var sent = Assert.Single(_notifier.Events);
        Assert.Equal(CustomPageEvents.RecordsRemoved, sent.Key);
        Assert.Equal("Record removed", sent.Title);
        Assert.Equal("Removed Big Buck Bunny from queue", sent.Body);
    }

    /// <summary>
    /// A cleared store is sent at once with who cleared it.
    /// True: the most consequential store change reaches Notifications without waiting.
    /// False: a cleared store waits behind a burst or is never sent.
    /// </summary>
    [Fact]
    public void StoreCleared_SendsAtOnce()
    {
        _activity.StoreCleared("queue", 4, "admin");

        var sent = Assert.Single(_notifier.Events);
        Assert.Equal(CustomPageEvents.StoreCleared, sent.Key);
        Assert.Equal("Store cleared", sent.Title);
        Assert.Equal("Admin cleared 4 records from queue", sent.Body);
        Assert.Equal("Necto.PagesStoreCleared", Assert.Single(_log.Entries).Type);
    }

    /// <summary>
    /// Every key the activity sends is in the list Notifications reads.
    /// True: Notifications can offer each Custom pages event on groups.
    /// False: an event is sent that no group can choose.
    /// </summary>
    [Fact]
    public void EveryKeySent_IsRegistered()
    {
        _activity.RecordAdded("queue", null);
        _activity.RecordRemoved("queue", null);
        _activity.Flush();
        _activity.StoreCleared("queue", 1);

        Assert.All(_notifier.Events, e => Assert.Contains(e.Key, CustomPageEvents.All));
        Assert.Equal(CustomPageEvents.All.Count, _notifier.Events.Select(e => e.Key).Distinct().Count());
        Assert.All(CustomPageEvents.All, k => Assert.StartsWith("pages.", k, StringComparison.Ordinal));
    }
}
