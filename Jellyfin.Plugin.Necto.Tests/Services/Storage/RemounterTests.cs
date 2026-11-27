using Jellyfin.Plugin.Necto.Configuration.Storage;
using Jellyfin.Plugin.Necto.Services.Storage;
using Xunit;

using Jellyfin.Plugin.Necto.Tests.Fakes.Storage;

namespace Jellyfin.Plugin.Necto.Tests.Services.Storage;

/// <summary>
/// Covers the pure parts of each OS's remounter: systemd's unit names, macOS's share URLs, and systemd's errors.
/// </summary>
public class RemounterTests
{
    [Theory]
    [InlineData("/mnt/media", "mnt-media.mount")]
    [InlineData("/", "-.mount")]
    [InlineData("/mnt/my disk", @"mnt-my\x20disk.mount")]
    [InlineData("/srv/nas-media/", @"srv-nas\x2dmedia.mount")]
    [InlineData("/mnt/.hidden", @"mnt-.hidden.mount")]
    [InlineData("/.hidden", @"\x2ehidden.mount")]
    [InlineData("/mnt/Füße", @"mnt-F\xc3\xbc\xc3\x9fe.mount")]
    public void SystemdNamesAMountUnitLikeSystemdEscape(string path, string unit)
        => Assert.Equal(unit, SystemdUnit.MountUnit(path));

    [Theory]
    [InlineData("smbfs", "//jp@nas._smb._tcp.local/Media", "smb://jp@nas._smb._tcp.local/Media")]
    [InlineData("afpfs", "//nas/Media", "afp://nas/Media")]
    [InlineData("nfs", "nas:/export/media", "nfs://nas/export/media")]
    [InlineData("webdav", "https://dav.example.com/media", "https://dav.example.com/media")]
    [InlineData("apfs", "/dev/disk3s1s1", null)]
    [InlineData("smbfs", "", null)]
    public void MacOSRemembersAShareAsTheUrlItCameFrom(string fileSystem, string from, string? url)
        => Assert.Equal(url, MacRemounter.ShareUrl(fileSystem, from));

    [Fact]
    public void ADiskOutsideFstabIsLeftAlone()
        => Assert.False(LinuxRemounter.Classify("org.freedesktop.systemd1.NoSuchUnit").Tried);

    [Fact]
    public void ARefusedStartSaysJellyfinIsNotAllowed()
        => Assert.Equal(RemountFailure.Denied, LinuxRemounter.Classify("org.freedesktop.DBus.Error.AccessDenied").Failure);
}
