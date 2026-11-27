using System;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Something worked out from the installed web client's folder for the anonymous request page: its stylesheets or
/// logo. Found, it is kept for as long as the server runs, since the web client only changes with a server update.
/// Not found, that is kept as well, but only for a few minutes: otherwise every anonymous hit on the page would scan
/// the folder again, while keeping it for good would leave a folder that was briefly unreadable broken until restart.
/// </summary>
/// <typeparam name="T">What is looked up.</typeparam>
internal sealed class WebAssetCache<T>
    where T : class
{
    /// <summary>How long nothing found is kept before the folder is looked at again.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private readonly Func<T?, bool> _isFound;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private T? _value;
    private bool _found;
    private DateTime? _checkedUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebAssetCache{T}"/> class.
    /// </summary>
    /// <param name="isFound">Whether a looked-up value counts as found, and so is kept for good.</param>
    /// <param name="clock">The time now, for tests; the system clock when null.</param>
    internal WebAssetCache(Func<T?, bool> isFound, Func<DateTime>? clock = null)
    {
        _isFound = isFound;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Returns the value, looking it up only the first time, and again after nothing was found a while ago.
    /// </summary>
    /// <param name="find">Looks the value up.</param>
    /// <returns>The value, which may be the not-found one.</returns>
    internal T? Get(Func<T?> find)
    {
        ArgumentNullException.ThrowIfNull(find);
        lock (_lock)
        {
            var now = _clock();
            if (_found || (_checkedUtc is { } at && now - at < RetryAfter))
            {
                return _value;
            }

            _value = find();
            _found = _isFound(_value);
            _checkedUtc = now;
            return _value;
        }
    }
}
