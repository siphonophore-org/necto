using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The requests made on each recent day, for the Dashboard's Requests chart. The two lists line up by index.
/// </summary>
public class RequestDays
{
    /// <summary>Gets or sets each day as yyyy-MM-dd in the server's time zone, oldest first.</summary>
    public List<string> Days { get; set; } = new();

    /// <summary>Gets or sets how many requests were made on each day.</summary>
    public List<int> Made { get; set; } = new();

    /// <summary>
    /// Counts the requests made on each of the last days, today included, in the server's time zone.
    /// </summary>
    /// <param name="createdUtc">When each request was made, in UTC.</param>
    /// <param name="today">Today in the server's time zone.</param>
    /// <param name="count">How many days to count.</param>
    /// <returns>The days, oldest first, each with its count.</returns>
    public static RequestDays Count(IEnumerable<DateTime> createdUtc, DateTime today, int count)
    {
        ArgumentNullException.ThrowIfNull(createdUtc);
        var first = today.Date.AddDays(1 - count);
        var result = new RequestDays();
        for (var i = 0; i < count; i++)
        {
            result.Days.Add(first.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            result.Made.Add(0);
        }

        foreach (var created in createdUtc)
        {
            var index = (int)(DateTime.SpecifyKind(created, DateTimeKind.Utc).ToLocalTime().Date - first).TotalDays;
            if (index >= 0 && index < count)
            {
                result.Made[index]++;
            }
        }

        return result;
    }
}
