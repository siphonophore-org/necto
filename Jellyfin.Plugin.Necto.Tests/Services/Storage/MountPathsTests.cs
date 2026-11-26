using System;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the path rules that place a folder on its mount point.
/// </summary>
public class MountPathsTests
{
    private const StringComparison Exact = StringComparison.Ordinal;

    [Theory]
    [InlineData("/mnt/media/", "/mnt/media")]
    [InlineData("/mnt/media//", "/mnt/media")]
    [InlineData("/", "/")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"D:\Movies\", @"D:\Movies")]
    public void NormalizeTrimsSeparatorsButKeepsRoots(string path, string expected)
        => Assert.Equal(expected, MountPaths.Normalize(path));

    [Theory]
    [InlineData("/mnt/media/Movies", "/mnt/media", true)]
    [InlineData("/mnt/media", "/mnt/media", true)]
    [InlineData("/mnt/media2/Movies", "/mnt/media", false)]
    [InlineData("/mnt/media/Movies", "/", true)]
    [InlineData(@"D:\Movies", @"D:\", true)]
    public void IsUnderMatchesWholeSegments(string path, string mount, bool expected)
        => Assert.Equal(expected, MountPaths.IsUnder(path, mount, Exact));

    [Fact]
    public void FindMountPicksTheDeepestMount()
    {
        var mounts = new[] { "/", "/mnt", "/mnt/media", "/mnt/media2" };

        Assert.Equal("/mnt/media", MountPaths.FindMount("/mnt/media/Movies", mounts, Exact));
        Assert.Equal("/", MountPaths.FindMount("/home/me/Movies", mounts, Exact));
    }

    [Fact]
    public void FindMountIgnoresCaseOnlyWhenAsked()
    {
        var mounts = new[] { "/", "/Volumes/Media" };

        Assert.Equal("/Volumes/Media", MountPaths.FindMount("/volumes/media/Shows", mounts, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("/", MountPaths.FindMount("/volumes/media/Shows", mounts, Exact));
    }

    [Theory]
    [InlineData(@"\\nas\media\Movies", @"\\nas\media")]
    [InlineData("//nas/media/Movies", "//nas/media")]
    [InlineData(@"\\nas", @"\\nas")]
    public void ShareRootKeepsServerAndShare(string path, string expected)
        => Assert.Equal(expected, MountPaths.ShareRoot(path));

    [Fact]
    public void FindMountTreatsANetworkPathAsItsShare()
        => Assert.Equal(@"\\nas\media", MountPaths.FindMount(@"\\nas\media\Movies", new[] { @"C:\" }, Exact));

    [Theory]
    [InlineData("/Volumes/Media/Shows/Andor", "/Volumes/Media")]
    [InlineData("/mnt/media/Movies", "/mnt/media")]
    [InlineData("/media/usb/Movies", "/media/usb")]
    [InlineData("/run/media/me/usb/Movies", "/run/media/me/usb")]
    [InlineData(@"E:\Movies", @"E:\")]
    [InlineData(@"\\nas\media\Movies", @"\\nas\media")]
    [InlineData("/home/me/Movies", null)]
    public void GuessMountPointUsesTheUsualPlaces(string path, string? expected)
        => Assert.Equal(expected, MountPaths.GuessMountPoint(path));

    [Theory]
    [InlineData("/Volumes/Media", null, "Media")]
    [InlineData("/", null, "/")]
    [InlineData(@"D:\", "Movies", "Movies")]
    [InlineData(@"\\nas\media", null, "media")]
    public void DisplayNamePrefersTheLabel(string mount, string? label, string expected)
        => Assert.Equal(expected, MountPaths.DisplayName(mount, label));

    [Fact]
    public void TheSystemMountPointIsTheRootOrTheWindowsDrive()
    {
        var expected = OperatingSystem.IsWindows() ? MountPaths.Normalize(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!) : "/";

        Assert.Equal(expected, MountPaths.SystemMountPoint);
    }

    [Theory]
    [InlineData("/mnt/media", "ext4", true)]
    [InlineData("/Volumes/Media", "smbfs", true)]
    [InlineData("/run/media/me/usb", "exfat", true)]
    [InlineData(@"D:\", "NTFS", true)]
    [InlineData("/dev/shm", "tmpfs", false)]
    [InlineData("/mnt/cache", "tmpfs", false)]
    [InlineData("/proc", "proc", false)]
    [InlineData("/run/user/1000", "ext4", false)]
    [InlineData("/System/Volumes/Data", "apfs", false)]
    [InlineData("/var/lib/docker/overlay2/abc/merged", "ext4", false)]
    [InlineData("/boot/efi", "vfat", false)]
    [InlineData("/private/var/run/com.apple.security.cryptexd/mnt/tvOS_24J360", "apfs", false)]
    [InlineData("/Users/me/Library/Developer/CoreDevice/DeviceFS", "devicefs", false)]
    [InlineData("/Users/me/Library/Developer/CoreSimulator/Volumes/iOS_22A", "apfs", false)]
    [InlineData("/Volumes/Recovery", "apfs", false)]
    [InlineData("/Volumes/Backup", "apfs", true)]
    public void IsUserVolumeLeavesOutVolumesTheOsKeeps(string mount, string fileSystem, bool expected)
        => Assert.Equal(expected, MountPaths.IsUserVolume(mount, fileSystem, 500L * 1024 * 1024 * 1024));

    [Fact]
    public void IsUserVolumeLeavesOutTinyVolumes()
        => Assert.False(MountPaths.IsUserVolume("/mnt/efi", "vfat", 512L * 1024 * 1024));
}
