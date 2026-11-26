using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the time limit on disk calls, and that a stuck call is never started twice.
/// </summary>
public class VolumeProbeTests
{
    [Fact]
    public async Task AFastCallReturnsItsValue()
    {
        var probe = new VolumeProbe();

        var result = await probe.RunAsync("a", () => 42, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task AStuckCallTimesOutAndIsNotStartedAgain()
    {
        var probe = new VolumeProbe();
        using var release = new ManualResetEventSlim(false);
        var starts = 0;
        bool Work()
        {
            Interlocked.Increment(ref starts);
            release.Wait();
            return true;
        }

        var first = await probe.RunAsync("share", Work, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        var second = await probe.RunAsync("share", Work, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.False(first.Completed);
        Assert.False(second.Completed);
        Assert.Equal(1, starts);
        Assert.True(probe.IsStuck("share"));

        release.Set();
        for (var i = 0; i < 100 && probe.IsStuck("share"); i++)
        {
            await Task.Delay(10);
        }

        var third = await probe.RunAsync("share", Work, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(third.Completed);
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task ACallItsCallerStoppedWaitingForIsNotStartedAgain()
    {
        var probe = new VolumeProbe();
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.RunAsync("format:disk4", () => release.Wait(Timeout.Infinite), TimeSpan.FromMinutes(1), cancel.Token));

        Assert.True(probe.IsStuck("format:disk4"));
        release.Set();
    }
}
