using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers keeping the Error alert past the Warning alert.
/// </summary>
public class AlertOrderTests
{
    private static StorageConfiguration Config(double warning, double error, SpaceUnit unit = SpaceUnit.Percent, bool errorOn = true)
        => new()
        {
            WarningStop = new SpaceStop { Value = warning, Unit = unit },
            ErrorStop = new SpaceStop { Value = error, Unit = unit, Enabled = errorOn }
        };

    [Fact]
    public void AnErrorBelowTheWarningMovesPastIt()
    {
        var config = Config(90, 80);
        AlertOrder.Normalize(config);
        Assert.Equal(90, config.WarningStop.Value);
        Assert.Equal(91, config.ErrorStop.Value);
    }

    [Fact]
    public void AWarningAtTheTopMakesRoomForTheError()
    {
        var config = Config(100, 95);
        AlertOrder.Normalize(config);
        Assert.Equal(99, config.WarningStop.Value);
        Assert.Equal(100, config.ErrorStop.Value);
    }

    [Fact]
    public void InGigabytesTheErrorKeepsLessFreeSpace()
    {
        var config = Config(100, 150, SpaceUnit.Gigabytes);
        AlertOrder.Normalize(config);
        Assert.Equal(90, config.ErrorStop.Value);
    }

    [Fact]
    public void AnOrderedPairIsLeftAlone()
    {
        var config = Config(90, 95);
        AlertOrder.Normalize(config);
        Assert.Equal(90, config.WarningStop.Value);
        Assert.Equal(95, config.ErrorStop.Value);
    }

    [Fact]
    public void AnAlertThatIsOffIsLeftAlone()
    {
        var config = Config(90, 80, errorOn: false);
        AlertOrder.Normalize(config);
        Assert.Equal(80, config.ErrorStop.Value);
    }
    [Fact]
    public void ADiskWithItsOwnAlertsUsesThem()
    {
        var config = Config(90, 95);
        config.DiskOverrides.Add(new DiskOverride { MountPoint = "/mnt/media/", WarningStop = new SpaceStop { Value = 70 }, ErrorStop = new SpaceStop { Value = 80 } });
        var (warning, error, own) = AlertOrder.For(config, "/mnt/media");
        Assert.True(own);
        Assert.Equal(70, warning.Value);
        Assert.Equal(80, error.Value);
    }

    [Fact]
    public void ADiskWithoutItsOwnAlertsUsesTheGlobalOnes()
    {
        var config = Config(90, 95);
        config.DiskOverrides.Add(new DiskOverride { MountPoint = "/mnt/other" });
        var (warning, _, own) = AlertOrder.For(config, "/mnt/media");
        Assert.False(own);
        Assert.Same(config.WarningStop, warning);
    }

    [Fact]
    public void SavingKeepsTheLastOverridePerDiskInOrder()
    {
        var config = Config(90, 95);
        config.DiskOverrides.Add(new DiskOverride { MountPoint = "/mnt/media", WarningStop = new SpaceStop { Value = 60 } });
        config.DiskOverrides.Add(new DiskOverride { MountPoint = "/mnt/media/", WarningStop = new SpaceStop { Value = 85 }, ErrorStop = new SpaceStop { Value = 80 } });
        config.DiskOverrides.Add(new DiskOverride { MountPoint = " " });
        AlertOrder.Normalize(config);
        var disk = Assert.Single(config.DiskOverrides);
        Assert.Equal("/mnt/media", disk.MountPoint);
        Assert.Equal(85, disk.WarningStop.Value);
        Assert.Equal(86, disk.ErrorStop.Value);
    }
}
