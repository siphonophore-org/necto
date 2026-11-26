using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Runs disk calls on their own thread with a time limit. A call on a dead share can block forever and cannot be
/// stopped, so a call still stuck from an earlier check, or one its caller stopped waiting for, is never started again
/// until it returns.
/// </summary>
public sealed class VolumeProbe
{
    private readonly ConcurrentDictionary<string, Task> _stuck = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs one disk call and waits for it up to the time limit.
    /// </summary>
    /// <typeparam name="T">What the call returns.</typeparam>
    /// <param name="key">Names the call, such as the path it reads, so a stuck call is not started twice.</param>
    /// <param name="work">The call. It catches its own expected errors and never throws.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Stops the wait, though not the call.</param>
    /// <returns>The value, or a result that did not complete when the call is stuck or ran out of time.</returns>
    public async Task<ProbeResult<T>> RunAsync<T>(string key, Func<T> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(work);
        if (_stuck.TryGetValue(key, out var pending) && !pending.IsCompleted)
        {
            return new ProbeResult<T>(false, default);
        }

        var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            var value = await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return new ProbeResult<T>(true, value);
        }
        catch (TimeoutException)
        {
            Track(key, task);
            return new ProbeResult<T>(false, default);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped waiting, but the call runs on, so it is not started twice while it does.
            Track(key, task);
            throw;
        }
    }

    private void Track(string key, Task task)
    {
        _stuck[key] = task;
        _ = task.ContinueWith(
            t =>
            {
                _ = t.Exception;
                _stuck.TryRemove(new(key, t));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Tells whether a call under this key is still stuck from an earlier check.
    /// </summary>
    /// <param name="key">The call's key.</param>
    /// <returns><c>true</c> while the call has not returned.</returns>
    public bool IsStuck(string key) => _stuck.TryGetValue(key, out var pending) && !pending.IsCompleted;
}
