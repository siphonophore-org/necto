using System.Runtime.InteropServices;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// The sidecar release names one file per platform the build makes, and reads its hash from SHA256SUMS.
/// </summary>
public class SidecarReleaseTests
{
    /// <summary>
    /// Each platform Jellyfin runs on maps to the name the sidecar build gives it.
    /// True: the download asks for a file the release has.
    /// False: the download asks for a file that does not exist and WireGuard never runs.
    /// </summary>
    [Theory]
    [InlineData("LINUX", Architecture.X64, "linux-amd64")]
    [InlineData("LINUX", Architecture.Arm64, "linux-arm64")]
    [InlineData("LINUX", Architecture.Arm, "linux-arm")]
    [InlineData("OSX", Architecture.Arm64, "darwin-arm64")]
    [InlineData("OSX", Architecture.X64, "darwin-amd64")]
    [InlineData("WINDOWS", Architecture.X64, "windows-amd64")]
    [InlineData("WINDOWS", Architecture.Arm64, "windows-arm64")]
    public void Platform_MatchesTheBuiltFiles(string os, Architecture arch, string expected)
        => Assert.Equal(expected, SidecarRelease.Platform(OSPlatform.Create(os), arch));

    /// <summary>
    /// A platform the build does not make has no file.
    /// True: Settings says no sidecar is built for the server.
    /// False: the download asks for a file that does not exist.
    /// </summary>
    [Theory]
    [InlineData("WINDOWS", Architecture.Arm)]
    [InlineData("OSX", Architecture.Arm)]
    [InlineData("FREEBSD", Architecture.X64)]
    [InlineData("LINUX", Architecture.X86)]
    public void Platform_IsNullWithoutABuild(string os, Architecture arch)
        => Assert.Null(SidecarRelease.Platform(OSPlatform.Create(os), arch));

    /// <summary>
    /// The file name matches the build script's, with .exe only on Windows.
    /// True: the download and the release agree on each file.
    /// False: the download misses the file.
    /// </summary>
    [Theory]
    [InlineData("linux-amd64", "wgsidecar-linux-amd64")]
    [InlineData("windows-arm64", "wgsidecar-windows-arm64.exe")]
    public void FileName_MatchesTheBuild(string platform, string expected)
        => Assert.Equal(expected, SidecarRelease.FileName(platform));

    /// <summary>
    /// The release address is the release named after the version, on the Necto repository.
    /// True: the download reaches the release the workflow publishes.
    /// False: the download reaches nothing.
    /// </summary>
    [Fact]
    public void BaseUri_IsTheVersionsRelease()
        => Assert.Equal("https://github.com/siphonophore-org/necto/releases/download/wgsidecar-1.0.0/", SidecarRelease.BaseUri("1.0.0").ToString());

    /// <summary>
    /// A file's hash is read from SHA256SUMS in either format sha256sum writes, and a file not listed has none.
    /// True: a download is checked against the right hash.
    /// False: a good file is refused or a file is kept unchecked.
    /// </summary>
    [Fact]
    public void HashOf_ReadsTheListedFile()
    {
        var a = new string('a', 64);
        var b = new string('B', 64);
        var sums = a + "  wgsidecar-linux-amd64\n" + b + " *wgsidecar-windows-amd64.exe\n";

        Assert.Equal(a, SidecarRelease.HashOf(sums, "wgsidecar-linux-amd64"));
        Assert.Equal(b.ToLowerInvariant(), SidecarRelease.HashOf(sums, "wgsidecar-windows-amd64.exe"));
        Assert.Null(SidecarRelease.HashOf(sums, "wgsidecar-darwin-arm64"));
    }
}
