using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Tasks.Storage;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using Jellyfin.Plugin.Necto.Tests.Services.Storage;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Tasks.Storage;

/// <summary>
/// The Check storage task runs under Necto every 30 minutes and at startup, by the key Troubleshooting runs it by.
/// </summary>
public sealed class CheckStorageTaskTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "storage-task-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckStorageTaskTests"/> class.
    /// </summary>
    public CheckStorageTaskTests() => Directory.CreateDirectory(_folder);

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_folder, true);

    /// <summary>
    /// The task has the key Troubleshooting runs, Necto's category, and its two triggers.
    /// True: Run now finds the task, and disks are checked without anyone opening Storage.
    /// False: a change would wait for someone to open the page.
    /// </summary>
    [Fact]
    public void Task_HasItsKeyCategoryAndTriggers()
    {
        using var storage = StorageServiceTests.Build(_folder, new StorageServiceTests.FakeMountTable(), new TestServices.Settings(), TestServices.Activity(out _));
        var task = new CheckStorageTask(storage, InterfaceStub.Create<IServerConfigurationManager>().Proxy);

        var triggers = task.GetDefaultTriggers().ToList();

        Assert.Equal("NectoStorageCheck", task.Key);
        Assert.Equal("Necto", task.Category);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.IntervalTrigger && t.IntervalTicks == TimeSpan.FromMinutes(30).Ticks);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.StartupTrigger);
    }

    /// <summary>
    /// A run checks the disks and reports it is done.
    /// True: the task finishes at 100 and the disk changes are recorded.
    /// False: the task would hang or never check.
    /// </summary>
    [Fact]
    public async Task Execute_ChecksTheDisks()
    {
        var mounts = new StorageServiceTests.FakeMountTable();
        using var storage = StorageServiceTests.Build(_folder, mounts, new TestServices.Settings(), TestServices.Activity(out var log));
        var task = new CheckStorageTask(storage, InterfaceStub.Create<IServerConfigurationManager>().Proxy);
        var done = 0d;
        await task.ExecuteAsync(new Progress<double>(v => done = v), CancellationToken.None);
        mounts.Mounted = false;

        await task.ExecuteAsync(new Progress<double>(v => done = v), CancellationToken.None);

        Assert.Contains(TestServices.Entries(log), e => e.Name == "Disk missing");
    }
}
