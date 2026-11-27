using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;
using Jellyfin.Plugin.Necto.Tasks.CustomPages;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Tasks.CustomPages;

/// <summary>
/// The store retention task applies every store's retention on a schedule, in the server's language, and stops when
/// cancelled.
/// </summary>
public class StoreRetentionTaskTests
{
    /// <summary>
    /// The task's name and description come from the strings.
    /// True: the scheduled tasks page names the task in the server's language.
    /// False: the task shows a key or an empty name.
    /// </summary>
    [Fact]
    public void NameAndDescription_ComeFromTheStrings()
    {
        var task = new StoreRetentionTask(new RecordingStoreService(), NullLogger<StoreRetentionTask>.Instance, FakeServerConfig.Create());

        Assert.Equal("Apply page store retention", task.Name);
        Assert.StartsWith("Removes records past the retention", task.Description, StringComparison.Ordinal);
        Assert.Equal("NectoPagesStoreRetention", task.Key);
    }

    /// <summary>
    /// The task hands its cancellation token to the sweep.
    /// True: cancelling the task stops the sweep between stores.
    /// False: a cancelled task keeps sweeping.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PassesItsCancellationTokenToTheSweep()
    {
        var stores = new RecordingStoreService();
        var task = new StoreRetentionTask(stores, NullLogger<StoreRetentionTask>.Instance, FakeServerConfig.Create());
        using var source = new CancellationTokenSource();

        await task.ExecuteAsync(new Progress<double>(), source.Token);

        Assert.Equal(new[] { source.Token }, stores.Sweeps);
    }

    /// <summary>
    /// A cancelled run stops with a cancellation, which the task manager tells apart from a failure.
    /// True: a cancelled run reads as cancelled.
    /// False: a cancelled run reads as failed or as done.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_CancelledRun_StopsWithACancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "necto-retention-" + Guid.NewGuid().ToString("N"));
        var (paths, stub) = InterfaceStub.Create<IApplicationPaths>();
        stub.Answer("get_DataPath", _ => root);
        var settings = new CustomPagesSettings();
        settings.Stores.Add(new PageStore { Name = "queue", RetentionDays = 1 });
        var service = new StoreService(paths, NullLogger<StoreService>.Instance, () => settings);
        var task = new StoreRetentionTask(service, NullLogger<StoreRetentionTask>.Instance, FakeServerConfig.Create());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.ExecuteAsync(new Progress<double>(), cancelled.Token));
    }

    /// <summary>
    /// The task runs every six hours by default.
    /// True: a store nobody touches is trimmed within six hours of a record expiring.
    /// False: expired records linger in idle stores.
    /// </summary>
    [Fact]
    public void GetDefaultTriggers_RunsEverySixHours()
    {
        var task = new StoreRetentionTask(new RecordingStoreService(), NullLogger<StoreRetentionTask>.Instance, FakeServerConfig.Create());

        var trigger = Assert.Single(task.GetDefaultTriggers().ToList());

        Assert.Equal(TimeSpan.FromHours(6).Ticks, trigger.IntervalTicks);
    }
}
