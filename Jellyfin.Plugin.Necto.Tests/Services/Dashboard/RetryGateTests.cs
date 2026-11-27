using System;
using Jellyfin.Plugin.Necto.Services.Dashboard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Dashboard;

/// <summary>
/// The gate lets slow work start once in each interval, however often the Dashboard loads.
/// </summary>
public class RetryGateTests
{
    /// <summary>
    /// The first claim passes, a second inside the interval does not, and one after it passes again.
    /// True: a location download or lookup starts at most once every few minutes.
    /// False: every Dashboard load would start another download.
    /// </summary>
    [Fact]
    public void TryClaim_PassesOncePerInterval()
    {
        var gate = new RetryGate(TimeSpan.FromMinutes(10));
        var start = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.True(gate.TryClaim(start));
        Assert.False(gate.TryClaim(start.AddMinutes(9)));
        Assert.True(gate.TryClaim(start.AddMinutes(10)));
        Assert.False(gate.TryClaim(start.AddMinutes(11)));
    }
}
