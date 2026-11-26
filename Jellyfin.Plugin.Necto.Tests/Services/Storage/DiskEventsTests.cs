using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using static Jellyfin.Plugin.Necto.Tests.Fakes.Storage.TestServices;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers which changes between two checks reach the activity log.
/// </summary>
public class DiskEventsTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, DateTime> NoneMissing = new Dictionary<string, DateTime>();

    [Fact]
    public void AnUnchangedDiskRecordsNothing()
    {
        var memory = new Dictionary<string, DiskMemory>();
        DiskEvents.Compare(new[] { Disk("/mnt/a") }, memory, NoneMissing, Now);

        Assert.Empty(DiskEvents.Compare(new[] { Disk("/mnt/a") }, memory, NoneMissing, Now));
    }

    [Fact]
    public void ADiskMissingAtFirstSightIsRecorded()
    {
        var events = DiskEvents.Compare(new[] { Disk("/mnt/a", DiskState.Missing) }, new Dictionary<string, DiskMemory>(), NoneMissing, Now);

        Assert.Equal(DiskEventKind.WentMissing, Assert.Single(events).Kind);
    }

    [Fact]
    public void ADiskThatComesBackSaysHowLongItWasAway()
    {
        var memory = new Dictionary<string, DiskMemory> { ["/mnt/a"] = new() { State = DiskState.Missing } };
        var since = new Dictionary<string, DateTime> { ["/mnt/a"] = Now.AddHours(-3) };

        var change = Assert.Single(DiskEvents.Compare(new[] { Disk("/mnt/a") }, memory, since, Now));

        Assert.Equal(DiskEventKind.Returned, change.Kind);
        Assert.Equal(TimeSpan.FromHours(3), change.Away);
    }

    [Fact]
    public void ADiskThatBecomesUnresponsiveIsRecorded()
    {
        var memory = new Dictionary<string, DiskMemory>();
        DiskEvents.Compare(new[] { Disk("/mnt/a") }, memory, NoneMissing, Now);

        var change = Assert.Single(DiskEvents.Compare(new[] { Disk("/mnt/a", DiskState.Unresponsive) }, memory, NoneMissing, Now));

        Assert.Equal(DiskEventKind.BecameUnresponsive, change.Kind);
    }

    [Fact]
    public void OnlyARisingLevelIsRecorded()
    {
        var memory = new Dictionary<string, DiskMemory>();
        var disk = Disk("/mnt/a", usedPercent: 91);
        disk.Level = SpaceLevel.Warning;
        Assert.Equal(DiskEventKind.Low, Assert.Single(DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now)).Kind);

        disk.Level = SpaceLevel.Error;
        Assert.Equal(DiskEventKind.Full, Assert.Single(DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now)).Kind);

        disk.Level = SpaceLevel.None;
        Assert.Empty(DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now));
    }

    [Fact]
    public void ADiskThatDropsOutKeepsItsLevelSoItIsNotRecordedTwice()
    {
        var memory = new Dictionary<string, DiskMemory>();
        var full = Disk("/mnt/a", usedPercent: 99);
        full.Level = SpaceLevel.Error;
        DiskEvents.Compare(new[] { full }, memory, NoneMissing, Now);
        DiskEvents.Compare(new[] { Disk("/mnt/a", DiskState.Missing) }, memory, NoneMissing, Now);

        var events = DiskEvents.Compare(new[] { full }, memory, NoneMissing, Now);

        Assert.Equal(new[] { DiskEventKind.Returned }, events.Select(e => e.Kind));
    }

    [Fact]
    public void AGoneDiskIsForgotten()
    {
        var memory = new Dictionary<string, DiskMemory>();
        DiskEvents.Compare(new[] { Disk("/mnt/a"), Disk("/mnt/b") }, memory, NoneMissing, Now);

        DiskEvents.Compare(new[] { Disk("/mnt/a") }, memory, NoneMissing, Now);

        Assert.Equal(new[] { "/mnt/a" }, memory.Keys);
    }
}
