using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;
using static Jellyfin.Plugin.Necto.Tests.Fakes.Storage.TestServices;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers a whole check: libraries in, disks out, with what changed written to the activity log and real-time
/// monitoring muted for a missing disk.
/// </summary>
public sealed class StorageServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "storage-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMountTable _mounts = new();
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Settings _settings = new();
    private readonly InterfaceStub _monitorCalls;
    private readonly ILibraryMonitor _monitor;
    private readonly FakeActivity _activityLog;
    private readonly StorageService _service;

    public StorageServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _monitor = Monitor(out _monitorCalls);
        var activity = Activity(out _activityLog);
        _service = Build(_folder, _mounts, _settings, activity, _time, _monitor);
    }

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_folder, true);
    }

    /// <summary>
    /// Builds a service over one library, Movies at /mnt/media/Movies, plus Collections, which is never checked.
    /// </summary>
    internal static StorageService Build(string folder, IMountTable mounts, StorageSettings settings, StorageActivity activity, TimeProvider? time = null, ILibraryMonitor? monitor = null, IDiskInventory? inventory = null, ISmartReader? smart = null, IRemounter? remounter = null, JellyfinFolders? jellyfin = null)
    {
        var paths = Paths(folder);
        var libraries = Libraries(new List<VirtualFolderInfo>
        {
            new() { Name = "Movies", ItemId = "m", Locations = new[] { "/mnt/media/Movies" }, CollectionType = CollectionTypeOptions.movies },
            new() { Name = "Collections", ItemId = "c", Locations = new[] { "/data/collections" }, CollectionType = CollectionTypeOptions.boxsets }
        }, out _);
        var muter = new MonitorMuter(monitor ?? Monitor(out _), NullLogger<MonitorMuter>.Instance);
        return new StorageService(libraries, mounts, inventory ?? new NoDiskInventory(), smart ?? new NoSmartReader(), remounter ?? new NoRemounter(), new VolumeProbe(), settings, activity, muter, paths, time ?? TimeProvider.System, NullLogger<StorageService>.Instance, jellyfin);
    }

    [Fact]
    public async Task AMountedDiskReportsItsSpace()
    {
        var disk = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal("/mnt/media", disk.MountPoint);
        Assert.Equal("media", disk.Name);
        Assert.Equal(DiskState.Available, disk.State);
        Assert.Equal(1000, disk.TotalBytes);
        Assert.Equal(250, disk.FreeBytes);
        Assert.Equal(SpaceLevel.None, disk.Level);
        Assert.Equal(90, disk.WarningPercent);
        Assert.Equal("Movies", Assert.Single(disk.Libraries).Name);
        Assert.Empty(Entries(_activityLog));
    }

    [Fact]
    public async Task ADiskNoLibraryUsesIsListedButNeverRecorded()
    {
        var disks = await _service.CheckAsync(CancellationToken.None);

        var spare = Assert.Single(disks, d => !d.InUse);
        Assert.Equal("/mnt/spare", spare.MountPoint);
        Assert.DoesNotContain(disks, d => d.MountPoint == "/dev/shm");
        Assert.True(disks[0].InUse);
        Assert.Empty(Entries(_activityLog));
    }

    [Fact]
    public async Task AnUnformattedDiskIsListedByItsDevice()
    {
        var inventory = new FakeDiskInventory { Unmounted = new[] { new RawDisk("/dev/sdb", "Samsung SSD 870", 2000) } };
        using var service = Build(_folder, _mounts, _settings, Activity(out _), inventory: inventory);

        var disk = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.State == DiskState.Unformatted);

        Assert.Equal("/dev/sdb", disk.Device);
        Assert.Equal("Samsung SSD 870", disk.Name);
        Assert.Equal(2000, disk.TotalBytes);
        Assert.False(disk.InUse);
    }

    [Fact]
    public async Task AFailingDiskReportsItsHealthAndIsRecordedOnce()
    {
        var smart = new FakeSmartReader();
        smart.Readings["/mnt/media"] = new SmartReading(DiskHealth.Good, 35, 1000);
        var activity = Activity(out var log);
        using var service = Build(_folder, _mounts, _settings, activity, smart: smart);
        Assert.Equal(DiskHealth.Good, Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse).Health);

        smart.Readings["/mnt/media"] = new SmartReading(DiskHealth.Failing, 61, 1001, 99);
        var disk = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse);
        await service.CheckAsync(CancellationToken.None);

        Assert.Equal(DiskHealth.Failing, disk.Health);
        Assert.Equal(61, disk.TemperatureCelsius);
        Assert.Equal(99, disk.WearPercent);
        var entry = Assert.Single(Entries(log), e => e.Name == "Disk failing");
        Assert.Equal("Media at /mnt/media reports a SMART failure", entry.ShortOverview);
    }

    [Fact]
    public async Task AMissingDiskKeepsTheTimeItWentMissing()
    {
        await _service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;

        var first = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);
        _time.Now = _time.Now.AddHours(2);
        var second = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal(DiskState.Missing, second.State);
        Assert.Equal(first.MissingSinceUtc, second.MissingSinceUtc);
        Assert.Equal(new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc), second.MissingSinceUtc);
        Assert.Null(second.TotalBytes);

        _mounts.Mounted = true;
        Assert.Null(Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse).MissingSinceUtc);
    }

    [Fact]
    public async Task ADiskThatLeavesAndComesBackIsRecordedOnceEach()
    {
        await _service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;
        await _service.CheckAsync(CancellationToken.None);
        await _service.CheckAsync(CancellationToken.None);
        _time.Now = _time.Now.AddHours(3);
        _mounts.Mounted = true;
        await _service.CheckAsync(CancellationToken.None);

        var entries = Entries(_activityLog);
        Assert.Equal(new[] { "Disk missing", "Disk back" }, entries.Select(e => e.Name));
        Assert.Equal("Media at /mnt/media is not mounted", entries[0].ShortOverview);
        Assert.Equal("Media at /mnt/media is back after 3 hours", entries[1].ShortOverview);
    }

    [Fact]
    public async Task ADiskPastAStopIsRecorded()
    {
        _settings.Value.WarningStop.Value = 70;
        var disk = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal(SpaceLevel.Warning, disk.Level);
        var entry = Assert.Single(Entries(_activityLog));
        Assert.Equal("Disk low on space", entry.Name);
        Assert.Equal("Media at /mnt/media is 75% full with 250.00 B free", entry.ShortOverview);
    }

    [Fact]
    public async Task MonitoringIsMutedWhileADiskIsMissing()
    {
        _mounts.Mounted = false;
        await _service.CheckAsync(CancellationToken.None);
        Assert.Equal(1, Count(_monitorCalls, "ReportFileSystemChangeBeginning", "/mnt/media/Movies"));

        _mounts.Mounted = true;
        await _service.CheckAsync(CancellationToken.None);
        Assert.Equal(1, Count(_monitorCalls, "ReportFileSystemChangeComplete", "/mnt/media/Movies"));
    }

    [Fact]
    public async Task MonitoringIsLeftAloneWhenMutingIsOff()
    {
        _settings.Value.MuteMonitoring = false;
        _mounts.Mounted = false;
        await _service.CheckAsync(CancellationToken.None);

        Assert.Equal(0, Count(_monitorCalls, "ReportFileSystemChangeBeginning"));
    }

    [Fact]
    public async Task AMissingDiskIsRemountedFromItsRememberedSource()
    {
        var remounter = new FakeRemounter();
        remounter.Sources["/mnt/media"] = "nfs://nas/media";
        remounter.Answer = (mount, source) =>
        {
            if (mount != "/mnt/media" || source != "nfs://nas/media")
            {
                return RemountResult.Failed(RemountFailure.Unreachable);
            }

            _mounts.Mounted = true;
            return RemountResult.Success;
        };
        var activity = Activity(out var log);
        using var service = Build(_folder, _mounts, _settings, activity, _time, remounter: remounter);
        await service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;

        var disk = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal(DiskState.Available, disk.State);
        var entry = Assert.Single(Entries(log));
        Assert.Equal("Disk remounted", entry.Name);
        Assert.Equal("Media at /mnt/media was remounted", entry.ShortOverview);
    }

    [Fact]
    public async Task AFailedRemountIsRecordedOnceThenPausesAfterThreeTries()
    {
        var remounter = new FakeRemounter { Answer = (_, _) => RemountResult.Failed(RemountFailure.SignIn, 80) };
        remounter.Sources["/mnt/media"] = "smb://nas/media";
        var activity = Activity(out var log);
        using var service = Build(_folder, _mounts, _settings, activity, _time, remounter: remounter);
        await service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;

        for (var i = 0; i < 5; i++)
        {
            await service.CheckAsync(CancellationToken.None);
        }

        Assert.Equal(3, remounter.Tries("/mnt/media", "smb://nas/media"));
        var entry = Assert.Single(Entries(log), e => e.Name == "Disk not remounted");
        Assert.Equal("Media at /mnt/media could not be remounted: the saved sign-in was refused", entry.ShortOverview);

        _time.Now = _time.Now.AddHours(1);
        await service.CheckAsync(CancellationToken.None);
        Assert.Equal(4, remounter.Tries("/mnt/media", "smb://nas/media"));
    }

    [Fact]
    public async Task RemountingCanBeTurnedOff()
    {
        var remounter = new FakeRemounter();
        _settings.Value.Remount = false;
        using var service = Build(_folder, _mounts, _settings, Activity(out _), _time, remounter: remounter);
        await service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;

        await service.CheckAsync(CancellationToken.None);

        Assert.Empty(remounter.Calls);
    }

    [Fact]
    public async Task ADiskWithItsOwnAlertsIsMeasuredByThem()
    {
        _settings.Value.DiskOverrides.Add(new DiskOverride { MountPoint = "/mnt/media", WarningStop = new SpaceStop { Value = 70 }, ErrorStop = new SpaceStop { Value = 80 } });

        var disk = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.True(disk.OwnAlerts);
        Assert.Equal(SpaceLevel.Warning, disk.Level);
        Assert.Equal(70, disk.WarningPercent);
    }

    [Fact]
    public async Task AnAddressSetByHandRemountsAShareNeverSeenMounted()
    {
        var remounter = new FakeRemounter { TakesAddress = true };
        remounter.Answer = (mount, source) =>
        {
            if (mount != "/mnt/media" || source != "smb://nas/media")
            {
                return RemountResult.Failed(RemountFailure.Unreachable);
            }

            _mounts.Mounted = true;
            return RemountResult.Success;
        };
        _mounts.Mounted = false;
        using var service = Build(_folder, _mounts, _settings, Activity(out _), _time, remounter: remounter);
        var missing = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse);
        Assert.Equal(DiskState.Missing, missing.State);
        Assert.Null(missing.ShareAddress);
        Assert.True(missing.TakesAddress);

        _settings.Value.ShareAddresses.Add(new ShareAddress { MountPoint = "/mnt/media", Address = "smb://nas/media" });
        var disk = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal(DiskState.Available, disk.State);
        Assert.Equal("smb://nas/media", disk.ShareAddress);
    }

    [Fact]
    public async Task ANewAddressIsTriedAtOnceDespiteThePause()
    {
        var remounter = new FakeRemounter { Answer = (_, _) => RemountResult.Failed(RemountFailure.Unreachable) };
        _settings.Value.ShareAddresses.Add(new ShareAddress { MountPoint = "/mnt/media", Address = "smb://old/media" });
        using var service = Build(_folder, _mounts, _settings, Activity(out _), _time, remounter: remounter);
        await service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;
        for (var i = 0; i < 4; i++)
        {
            await service.CheckAsync(CancellationToken.None);
        }

        Assert.Equal(3, remounter.Tries("/mnt/media", "smb://old/media"));
        _settings.Value.ShareAddresses[0].Address = "smb://new/media";
        await service.CheckAsync(CancellationToken.None);
        Assert.Equal(1, remounter.Tries("/mnt/media", "smb://new/media"));
    }

    [Fact]
    public void TheSystemDiskIsNamedAsSuchInTheActivityLog()
        => Assert.Equal("System disk at /", StorageActivity.Label(new DiskInfo { MountPoint = "/", Name = "/", IsSystem = true }, null));

    [Fact]
    public async Task BeforeAnyCheckFinishesTheDisksLastSeenMissingAreKnownAsUnsafe()
    {
        await _service.CheckAsync(CancellationToken.None);
        _mounts.Mounted = false;
        await _service.CheckAsync(CancellationToken.None);
        using var restarted = Build(_folder, _mounts, _settings, Activity(out _), _time);

        var known = Assert.Single(restarted.Known);

        Assert.Equal("/mnt/media", known.MountPoint);
        Assert.Equal(DiskState.Missing, known.State);
        Assert.False(known.ScanSafe);
    }

    [Fact]
    public async Task AUsbDiskIsExternalAndStaysSoWhileMissing()
    {
        var inventory = new FakeDiskInventory();
        inventory.External.Add("/mnt/media");
        using var service = Build(_folder, _mounts, _settings, Activity(out _), _time, inventory: inventory);
        Assert.Equal(DiskKind.External, Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse).Kind);
        _mounts.Mounted = false;

        Assert.Equal(DiskKind.External, Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.InUse).Kind);
    }

    [Fact]
    public async Task AFolderJellyfinCannotWriteToIsListed()
    {
        _mounts.ReadOnly = true;

        var disk = Assert.Single(await _service.CheckAsync(CancellationToken.None), d => d.InUse);

        Assert.Equal(new[] { "/mnt/media/Movies" }, disk.ReadOnlyFolders);
    }

    [Fact]
    public async Task ADiskWithOnlyJellyfinsCacheIsUsedByJellyfin()
    {
        using var service = Build(_folder, _mounts, _settings, Activity(out _), _time, jellyfin: new CacheOnSpare());

        var spare = Assert.Single(await service.CheckAsync(CancellationToken.None), d => d.MountPoint == "/mnt/spare");

        Assert.True(spare.InUse);
        Assert.Empty(spare.Libraries);
        Assert.Contains(spare.Folders, f => f.Server && f.Library == "LabelCache");
    }

    /// <summary>
    /// One disk at /mnt/media, which can be taken away. Leftover files keep its folder present while it is away.
    /// </summary>
    internal sealed class FakeMountTable : IMountTable
    {
        public bool Mounted { get; set; } = true;

        public bool LeftoverFiles { get; set; }

        public bool ReadOnly { get; set; }

        public bool CanWrite(string path) => !ReadOnly;

        public IReadOnlyList<string> GetMountPoints() => Mounted ? new[] { "/", "/mnt/media", "/mnt/spare", "/dev/shm" } : new[] { "/", "/mnt/spare", "/dev/shm" };

        public VolumeReading? Read(string mountPoint) => mountPoint switch
        {
            "/mnt/spare" => new(Spare, Spare / 2, "ext4", false, null),
            "/dev/shm" => new(Spare, Spare, "tmpfs", false, null),
            _ => new(1000, 250, "ext4", false, null)
        };

        private const long Spare = 100L * 1024 * 1024 * 1024;

        public FolderStatus GetFolder(string path)
            => Mounted || LeftoverFiles || !path.StartsWith("/mnt/media", StringComparison.Ordinal) ? FolderStatus.Present : FolderStatus.Missing;
    }

    private sealed class ManualTime : TimeProvider
    {
        public ManualTime(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CacheOnSpare : JellyfinFolders
    {
        public override IReadOnlyList<(string Key, string Path)> Get() => new[] { ("LabelCache", "/mnt/spare/cache") };
    }
}
