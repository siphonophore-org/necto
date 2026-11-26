using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the disk health alerts: too hot with a cooling margin, worn out, and failing, each recorded once.
/// </summary>
public class HealthAlertTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<string, DateTime> NoneMissing = new Dictionary<string, DateTime>();

    [Theory]
    [InlineData(59, false, false)]
    [InlineData(60, false, true)]
    [InlineData(56, true, true)]
    [InlineData(54, true, false)]
    [InlineData(null, true, false)]
    public void AHotDiskClearsOnlyOnceItHasCooled(int? celsius, bool wasHot, bool hot)
        => Assert.Equal(hot, HealthLevels.IsHot(celsius, new HealthAlerts(), wasHot));

    [Fact]
    public void AnAlertThatIsOffNeverFires()
    {
        var off = new HealthAlerts { Hot = false, Worn = false };
        Assert.False(HealthLevels.IsHot(90, off, false));
        Assert.False(HealthLevels.IsWorn(100, off));
    }

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    [InlineData(null, false)]
    public void AnSsdIsWornOutPastItsAlert(int? wear, bool worn) => Assert.Equal(worn, HealthLevels.IsWorn(wear, new HealthAlerts()));

    [Fact]
    public void AHotAndWornDiskIsRecordedOnceEach()
    {
        var memory = new Dictionary<string, DiskMemory>();
        var disk = Disk(hot: true, worn: true);

        var first = DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now);
        var second = DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now);

        Assert.Equal(new[] { DiskEventKind.Hot, DiskEventKind.Worn }, first.Select(e => e.Kind).ToArray());
        Assert.Empty(second);
    }

    [Fact]
    public void AFailingDiskIsNotRecordedWhenThatAlertIsOff()
    {
        var disk = Disk();
        disk.Health = DiskHealth.Failing;

        var events = DiskEvents.Compare(new[] { disk }, new Dictionary<string, DiskMemory>(), NoneMissing, Now, new HealthAlerts { Failing = false });

        Assert.Empty(events);
    }

    [Fact]
    public void AMissingDiskKeepsWhetherItWasHot()
    {
        var memory = new Dictionary<string, DiskMemory>();
        DiskEvents.Compare(new[] { Disk(hot: true) }, memory, NoneMissing, Now);
        DiskEvents.Compare(new[] { Disk(state: DiskState.Missing) }, memory, NoneMissing, Now);

        var back = DiskEvents.Compare(new[] { Disk(hot: true) }, memory, NoneMissing, Now);

        Assert.DoesNotContain(back, e => e.Kind == DiskEventKind.Hot);
    }

    [Fact]
    public void SavingKeepsTheHealthAlertsInRange()
    {
        var config = new StorageConfiguration { HealthAlerts = new HealthAlerts { HotCelsius = 200, WornPercent = 5 } };
        AlertOrder.Normalize(config);
        Assert.Equal(80, config.HealthAlerts.HotCelsius);
        Assert.Equal(50, config.HealthAlerts.WornPercent);
    }

    private static DiskInfo Disk(bool hot = false, bool worn = false, DiskState state = DiskState.Available)
        => new() { MountPoint = "/mnt/a", Name = "a", InUse = true, State = state, Hot = hot, Worn = worn, TemperatureCelsius = 65, WearPercent = 95 };

    [Fact]
    public void AFailingDiskIsRecordedOnceTheAlertIsTurnedOn()
    {
        var memory = new Dictionary<string, DiskMemory>();
        var disk = Disk();
        disk.Health = DiskHealth.Failing;
        DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now, new HealthAlerts { Failing = false });

        var events = DiskEvents.Compare(new[] { disk }, memory, NoneMissing, Now, new HealthAlerts());

        Assert.Contains(events, e => e.Kind == DiskEventKind.Failing);
    }
}
