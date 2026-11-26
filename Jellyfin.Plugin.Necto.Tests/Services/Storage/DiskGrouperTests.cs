using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers how library folders are placed on disks and when a disk counts as missing.
/// </summary>
public class DiskGrouperTests
{
    private static readonly string[] Mounts = { "/", "/mnt/media1", "/mnt/media2" };

    private static IReadOnlyList<DiskGroup> Group(IEnumerable<LibraryFolder> folders, IReadOnlyCollection<string> mounts, Func<string, FolderStatus?> status, StorageState state)
        => DiskGrouper.Group(folders, mounts, status, state, StringComparison.Ordinal);

    [Fact]
    public void FoldersOnOneDiskShareARow()
    {
        var folders = new[]
        {
            new LibraryFolder("m", "Movies", "/mnt/media1/Movies"),
            new LibraryFolder("s", "Shows", "/mnt/media1/Shows/"),
            new LibraryFolder("m", "Movies", "/mnt/media1/More movies")
        };

        var groups = Group(folders, Mounts, _ => FolderStatus.Present, new StorageState());

        var disk = Assert.Single(groups);
        Assert.Equal("/mnt/media1", disk.MountPoint);
        Assert.Equal(DiskState.Available, disk.State);
        Assert.Equal(new[] { "Movies", "Shows" }, disk.Libraries.Select(l => l.Name));
        Assert.Equal(3, disk.Folders.Count);
    }

    [Fact]
    public void ALibraryAcrossDisksAppearsOnEach()
    {
        var folders = new[]
        {
            new LibraryFolder("s", "Shows", "/home/me/Shows"),
            new LibraryFolder("s", "Shows", "/mnt/media2/Shows")
        };

        var groups = Group(folders, Mounts, _ => FolderStatus.Present, new StorageState());

        Assert.Equal(new[] { "/", "/mnt/media2" }, groups.Select(g => g.MountPoint).OrderBy(m => m, StringComparer.Ordinal));
        Assert.All(groups, g => Assert.Equal("Shows", Assert.Single(g.Libraries).Name));
    }

    [Fact]
    public void AGoneFolderInAUsualMountPlaceIsAMissingDisk()
    {
        var folders = new[] { new LibraryFolder("s", "Shows", "/Volumes/Media/Shows/Andor") };

        var groups = Group(folders, new[] { "/" }, _ => FolderStatus.Missing, new StorageState());

        var disk = Assert.Single(groups);
        Assert.Equal("/Volumes/Media", disk.MountPoint);
        Assert.Equal(DiskState.Missing, disk.State);
    }

    [Fact]
    public void ADiskThatLeftItsMountPointIsMissingEvenWithLeftoverFiles()
    {
        var state = new StorageState();
        var folders = new[] { new LibraryFolder("m", "Movies", "/srv/movies") };
        Group(folders, new[] { "/", "/srv/movies" }, _ => FolderStatus.Present, state);

        var groups = Group(folders, new[] { "/" }, _ => FolderStatus.Present, state);

        var disk = Assert.Single(groups);
        Assert.Equal("/srv/movies", disk.MountPoint);
        Assert.Equal(DiskState.Missing, disk.State);
        Assert.True(disk.Unguarded);
    }

    [Fact]
    public void AMissingDiskWhoseFoldersAreGoneOrEmptyIsGuardedByJellyfin()
    {
        var state = new StorageState();
        var folders = new[] { new LibraryFolder("m", "Movies", "/srv/movies"), new LibraryFolder("s", "Shows", "/srv/movies/Shows") };
        Group(folders, new[] { "/", "/srv/movies" }, _ => FolderStatus.Present, state);

        var groups = Group(folders, new[] { "/" }, p => p == "/srv/movies" ? FolderStatus.Empty : FolderStatus.Missing, state);

        var disk = Assert.Single(groups);
        Assert.Equal(DiskState.Missing, disk.State);
        Assert.False(disk.Unguarded);
    }

    [Fact]
    public void AGoneFolderOnAMountedDiskKeepsTheDiskMounted()
    {
        var folders = new[]
        {
            new LibraryFolder("m", "Movies", "/mnt/media1/Movies"),
            new LibraryFolder("m", "Movies", "/mnt/media1/Old")
        };

        var groups = Group(folders, Mounts, p => p == "/mnt/media1/Old" ? FolderStatus.Missing : FolderStatus.Present, new StorageState());

        var disk = Assert.Single(groups);
        Assert.Equal(DiskState.Available, disk.State);
        Assert.Equal(new[] { "/mnt/media1/Old" }, disk.MissingFolders);
    }

    [Fact]
    public void AFolderThatDoesNotAnswerMakesItsDiskUnresponsive()
    {
        var folders = new[] { new LibraryFolder("m", "Music", "/mnt/media2/Music") };

        var groups = Group(folders, Mounts, _ => (FolderStatus?)null, new StorageState());

        Assert.Equal(DiskState.Unresponsive, Assert.Single(groups).State);
    }

    [Fact]
    public void AGoneShareFolderIsAMissingShare()
    {
        var folders = new[] { new LibraryFolder("m", "Music", @"\\nas\music\Albums") };

        var groups = Group(folders, new[] { @"C:\" }, _ => FolderStatus.Missing, new StorageState());

        var disk = Assert.Single(groups);
        Assert.Equal(@"\\nas\music", disk.MountPoint);
        Assert.Equal(DiskState.Missing, disk.State);
    }

    [Fact]
    public void RememberedMountsKeepOnlyCurrentFolders()
    {
        var state = new StorageState();
        state.FolderMounts["/mnt/media1/Gone"] = "/mnt/media1";

        Group(new[] { new LibraryFolder("m", "Movies", "/mnt/media1/Movies") }, Mounts, _ => FolderStatus.Present, state);

        Assert.Equal("/mnt/media1", Assert.Single(state.FolderMounts, p => p.Key == "/mnt/media1/Movies").Value);
        Assert.DoesNotContain("/mnt/media1/Gone", state.FolderMounts.Keys);
    }

    [Fact]
    public void JellyfinsOwnFoldersPutTheirDiskInUseWithoutALibrary()
    {
        var folders = new[]
        {
            new LibraryFolder("jellyfin", "LabelCache", "/var/cache/jellyfin", true),
            new LibraryFolder("jellyfin", "LabelTranscodes", "/var/lib/jellyfin/transcodes", true)
        };

        // Transcodes is made at the first transcode, so it is not there yet.
        var group = Assert.Single(Group(folders, Mounts, p => p.EndsWith("transcodes", StringComparison.Ordinal) ? FolderStatus.Missing : FolderStatus.Present, new StorageState()));

        Assert.Equal("/", group.MountPoint);
        Assert.Equal(DiskState.Available, group.State);
        Assert.Empty(group.Libraries);
        Assert.Empty(group.MissingFolders);
        Assert.Equal(2, group.Folders.Count(f => f.Server));
    }

    [Fact]
    public void AMissingCacheDiskNeverBlocksALibraryScan()
    {
        var state = new StorageState();
        state.FolderMounts["/mnt/media2/cache"] = "/mnt/media2";

        var group = Assert.Single(Group(new[] { new LibraryFolder("jellyfin", "LabelCache", "/mnt/media2/cache", true) }, new[] { "/" }, _ => FolderStatus.Present, state));

        Assert.Equal(DiskState.Missing, group.State);
        Assert.False(group.Unguarded);
    }
}
