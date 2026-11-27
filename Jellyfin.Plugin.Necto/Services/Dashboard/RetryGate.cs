using System;
using System.Threading;

namespace Jellyfin.Plugin.Necto.Services.Dashboard;

/// <summary>
/// Lets a slow piece of work start at most once in each interval, however often the Dashboard asks for it.
/// </summary>
public sealed class RetryGate
{
    private readonly TimeSpan _interval;
    private long _nextTicks = DateTimeOffset.MinValue.UtcTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryGate"/> class.
    /// </summary>
    /// <param name="interval">The least time between two starts.</param>
    public RetryGate(TimeSpan interval)
    {
        _interval = interval;
    }

    /// <summary>
    /// Claims a start, when the interval since the last one has passed.
    /// </summary>
    /// <param name="now">The time now.</param>
    /// <returns><c>true</c> when the caller may start the work, and <c>false</c> when it started too recently.</returns>
    public bool TryClaim(DateTimeOffset now)
    {
        var next = Interlocked.Read(ref _nextTicks);
        if (now.UtcTicks < next)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _nextTicks, (now + _interval).UtcTicks, next) == next;
    }
}
