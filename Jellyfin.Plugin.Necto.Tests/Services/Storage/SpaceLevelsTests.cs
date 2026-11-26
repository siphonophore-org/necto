using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the Warning and Error stops, in both units, and the margin before a passed stop clears.
/// </summary>
public class SpaceLevelsTests
{
    private const long Total = 1000L * 1024 * 1024 * 1024;

    private static readonly SpaceStop Warning = new() { Value = 90 };
    private static readonly SpaceStop Error = new() { Value = 95 };

    private static SpaceLevel At(double usedPercent, SpaceLevel previous = SpaceLevel.None, SpaceStop? warning = null, SpaceStop? error = null)
        => SpaceLevels.Evaluate(Total, (long)(Total * (100 - usedPercent) / 100), warning ?? Warning, error ?? Error, previous);

    [Theory]
    [InlineData(50, SpaceLevel.None)]
    [InlineData(90, SpaceLevel.Warning)]
    [InlineData(94.9, SpaceLevel.Warning)]
    [InlineData(95, SpaceLevel.Error)]
    [InlineData(100, SpaceLevel.Error)]
    public void APercentStopIsReachedAtItsShare(double used, SpaceLevel expected)
        => Assert.Equal(expected, At(used));

    [Fact]
    public void APassedStopHoldsUntilTheDiskFallsAMarginBelow()
    {
        Assert.Equal(SpaceLevel.Warning, At(89, SpaceLevel.Warning));
        Assert.Equal(SpaceLevel.None, At(87.9, SpaceLevel.Warning));
        Assert.Equal(SpaceLevel.Error, At(93.5, SpaceLevel.Error));
        Assert.Equal(SpaceLevel.Warning, At(92.9, SpaceLevel.Error));
    }

    [Fact]
    public void AStopThatIsOffIsNeverReached()
    {
        var off = new SpaceStop { Enabled = false, Value = 95 };

        Assert.Equal(SpaceLevel.Warning, At(99, error: off));
        Assert.Null(SpaceLevels.Threshold(off, Total));
    }

    [Fact]
    public void AGigabyteStopCountsFreeSpace()
    {
        var warning = new SpaceStop { Value = 100, Unit = SpaceUnit.Gigabytes };
        var error = new SpaceStop { Value = 20, Unit = SpaceUnit.Gigabytes };

        Assert.Equal(90, SpaceLevels.Threshold(warning, Total)!.Value, 6);
        Assert.Equal(SpaceLevel.None, At(85, warning: warning, error: error));
        Assert.Equal(SpaceLevel.Warning, At(91, warning: warning, error: error));
        Assert.Equal(SpaceLevel.Error, At(98.5, warning: warning, error: error));
    }

    [Fact]
    public void AGigabyteStopLargerThanTheDiskIsReachedAtOnce()
        => Assert.Equal(0, SpaceLevels.Threshold(new SpaceStop { Value = 5000, Unit = SpaceUnit.Gigabytes }, Total));
}
