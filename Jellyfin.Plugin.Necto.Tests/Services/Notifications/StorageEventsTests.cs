using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Storage;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Notifications;

/// <summary>
/// Storage registers every event it sends, and each disk alert reaches Notifications with its key and level.
/// </summary>
public class StorageEventsTests
{
    /// <summary>
    /// Every key is unique and starts with the tool's key.
    /// True: Notifications lists each Storage event once, under Storage.
    /// False: two events would share a key, or one would list under another tool.
    /// </summary>
    [Fact]
    public void All_IsUniqueAndStorageScoped()
    {
        Assert.Equal(StorageEvents.All.Count, StorageEvents.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(StorageEvents.All, key => Assert.StartsWith("storage.", key, StringComparison.Ordinal));
        Assert.Contains("storage.disk_missing", StorageEvents.All);
        Assert.Contains("storage.smart_warning", StorageEvents.All);
        Assert.Contains("storage.space_low", StorageEvents.All);
    }

    /// <summary>
    /// Each disk change maps to a registered key.
    /// True: no change is sent under a key Notifications does not list.
    /// False: an alert would reach no one, since no group could choose it.
    /// </summary>
    [Theory]
    [InlineData(DiskEventKind.WentMissing, "storage.disk_missing")]
    [InlineData(DiskEventKind.BecameUnresponsive, "storage.disk_unresponsive")]
    [InlineData(DiskEventKind.Returned, "storage.disk_back")]
    [InlineData(DiskEventKind.Failing, "storage.smart_warning")]
    [InlineData(DiskEventKind.Hot, "storage.smart_warning")]
    [InlineData(DiskEventKind.Worn, "storage.smart_warning")]
    [InlineData(DiskEventKind.Low, "storage.space_low")]
    [InlineData(DiskEventKind.Full, "storage.space_full")]
    public void For_MapsEachKind_ToARegisteredKey(DiskEventKind kind, string key)
    {
        Assert.Equal(key, StorageEvents.For(kind));
        Assert.Contains(StorageEvents.For(kind), StorageEvents.All);
    }

    /// <summary>
    /// A check's changes go to Notifications once per kind, with the activity entry's words and a level.
    /// True: three shares that drop at once send one event, an error.
    /// False: a burst would flood every channel, or an alert would be lost.
    /// </summary>
    [Fact]
    public void Record_SendsOneEventPerKind_WithItsLevel()
    {
        var activity = TestServices.Activity(out var log, out var notifier);
        var events = new[]
        {
            new DiskEvent(DiskEventKind.WentMissing, TestServices.Disk("/mnt/a", DiskState.Missing), null),
            new DiskEvent(DiskEventKind.WentMissing, TestServices.Disk("/mnt/b", DiskState.Missing), null),
            new DiskEvent(DiskEventKind.Low, TestServices.Disk("/mnt/c", usedPercent: 92), null)
        };

        activity.Record(events);

        Assert.Equal(2, TestServices.Entries(log).Count);
        Assert.Collection(
            notifier.Events,
            e =>
            {
                Assert.Equal("storage.disk_missing", e.Key);
                Assert.Equal("Disks missing", e.Title);
                Assert.Equal(NectoEventLevel.Error, e.Level);
            },
            e =>
            {
                Assert.Equal("storage.space_low", e.Key);
                Assert.Equal(NectoEventLevel.Warning, e.Level);
            });
    }

    /// <summary>
    /// A blocked scan and a failed remount reach Notifications, and a disk with nothing to report sends nothing.
    /// True: the alerts the activity log records also reach the groups that choose them.
    /// False: a blocked scan would only show in the activity log.
    /// </summary>
    [Fact]
    public void ScanBlockedAndNotRemounted_SendTheirEvents()
    {
        var activity = TestServices.Activity(out _, out var notifier);
        var disk = TestServices.Disk("/mnt/a", DiskState.Missing);

        activity.ScanBlocked(new[] { disk });
        activity.NotRemounted(new[] { (disk, RemountResult.Failed(RemountFailure.SignIn)) });
        activity.Remounted(Array.Empty<DiskInfo>());

        Assert.Equal(new[] { StorageEvents.ScanBlocked, StorageEvents.RemountFailed }, notifier.Events.Select(e => e.Key));
    }
}
