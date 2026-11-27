using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Requests.Models;

/// <summary>
/// The requests of each recent day are counted on the server's local day, always a full run of days.
/// </summary>
public class RequestDaysTests
{
    /// <summary>
    /// Each request counts on its local day, days without requests count 0, and requests outside the window are left out.
    /// True: the Dashboard's Requests chart shows 14 columns with each day's requests.
    /// False: a quiet day would be skipped, or an old request would count today.
    /// </summary>
    [Fact]
    public void Count_CountsEachDayAndKeepsEmptyOnes()
    {
        var today = new DateTime(2026, 6, 15, 18, 0, 0, DateTimeKind.Local);
        var created = new[]
        {
            today.ToUniversalTime(),
            today.AddHours(-1).ToUniversalTime(),
            today.AddDays(-3).ToUniversalTime(),
            today.AddDays(-20).ToUniversalTime()
        };

        var days = RequestDays.Count(created, today, 14);

        Assert.Equal(14, days.Days.Count);
        Assert.Equal("2026-06-02", days.Days[0]);
        Assert.Equal("2026-06-15", days.Days[13]);
        Assert.Equal(2, days.Made[13]);
        Assert.Equal(1, days.Made[10]);
        Assert.Equal(3, days.Made.Sum());
    }

    /// <summary>
    /// No requests still gives every day at 0.
    /// True: an idle fortnight shows its baseline.
    /// False: the chart would be empty or short.
    /// </summary>
    [Fact]
    public void Count_NoRequests_GivesZeros()
    {
        var days = RequestDays.Count(Array.Empty<DateTime>(), new DateTime(2026, 6, 15), 14);

        Assert.Equal(14, days.Made.Count);
        Assert.All(days.Made, m => Assert.Equal(0, m));
    }
}
