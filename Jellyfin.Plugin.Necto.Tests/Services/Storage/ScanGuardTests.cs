using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using static Jellyfin.Plugin.Necto.Tests.Fakes.Storage.TestServices;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers which disks block a library scan in each mode, and that the guard cancels only the library scan.
/// </summary>
public class ScanGuardTests
{
    private static DiskInfo Unsafe(string mount)
    {
        var disk = Disk(mount, DiskState.Missing);
        disk.ScanSafe = false;
        return disk;
    }

    [Fact]
    public void AffectedBlocksOnlyForADiskJellyfinWouldScan()
    {
        var disks = new[] { Disk("/mnt/a"), Disk("/mnt/b", DiskState.Missing), Unsafe("/mnt/c") };

        var blockers = ScanGuardPolicy.Blockers(ScanGuardMode.Affected, disks);

        Assert.Equal(new[] { "/mnt/c" }, blockers.Select(d => d.MountPoint));
    }

    [Fact]
    public void AllBlocksForAnyDiskThatCannotBeRead()
    {
        var disks = new[] { Disk("/mnt/a"), Disk("/mnt/b", DiskState.Missing), Disk("/mnt/c", DiskState.Unresponsive) };

        Assert.Equal(2, ScanGuardPolicy.Blockers(ScanGuardMode.All, disks).Count);
    }

    [Fact]
    public void OffNeverBlocks()
        => Assert.Empty(ScanGuardPolicy.Blockers(ScanGuardMode.Off, new[] { Unsafe("/mnt/c") }));

    [Fact]
    public async Task TheGuardCancelsALibraryScanAndRecordsIt()
    {
        using var harness = new Harness();
        harness.Mounts.Mounted = false;
        await harness.Storage.CheckAsync(default);

        var worker = Worker(ScanGuard.LibraryScanKey);
        var blockers = harness.Guard.Guard(worker);

        Assert.Single(blockers);
        Assert.Equal(1, Count(harness.TaskCalls, "Cancel", worker));
        Assert.Contains(Entries(harness.ActivityLog), e => e.Name == "Library scan blocked");
    }

    [Fact]
    public void TheGuardLetsOtherTasksRun()
    {
        using var harness = new Harness();
        harness.Mounts.Mounted = false;

        var worker = Worker("CleanActivityLog");

        Assert.Empty(harness.Guard.Guard(worker));
        Assert.Equal(0, Count(harness.TaskCalls, "Cancel"));
    }

    private static IScheduledTaskWorker Worker(string key)
    {
        var (task, taskStub) = InterfaceStub.Create<IScheduledTask>();
        taskStub.Answer("get_Key", _ => key);
        var (worker, workerStub) = InterfaceStub.Create<IScheduledTaskWorker>();
        workerStub.Answer("get_ScheduledTask", _ => task);
        return worker;
    }

    // A missing disk whose folder still holds leftover files, which Jellyfin would scan and empty.
    private sealed class Harness : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "storage-guard-" + Guid.NewGuid().ToString("N"));

        public Harness()
        {
            Directory.CreateDirectory(_folder);
            var (tasks, taskCalls) = InterfaceStub.Create<ITaskManager>();
            Tasks = tasks;
            TaskCalls = taskCalls;
            Activity = Activity(out var log);
            ActivityLog = log;
            Storage = StorageServiceTests.Build(_folder, Mounts, Settings, Activity);
            Guard = new ScanGuard(Tasks, Storage, Settings, Activity, NullLogger<ScanGuard>.Instance);
            Storage.CheckAsync(default).GetAwaiter().GetResult();
        }

        public StorageServiceTests.FakeMountTable Mounts { get; } = new() { LeftoverFiles = true };

        public Settings Settings { get; } = new();

        public StorageActivity Activity { get; }

        public FakeActivity ActivityLog { get; }

        public ITaskManager Tasks { get; }

        public InterfaceStub TaskCalls { get; }

        public StorageService Storage { get; }

        public ScanGuard Guard { get; }

        public void Dispose()
        {
            Storage.Dispose();
            Directory.Delete(_folder, true);
        }
    }
}
