using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// The sidecar store keeps a downloaded sidecar only when its hash matches, and makes it executable.
/// </summary>
public sealed class SidecarStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "necto-sidecar-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Nothing is found before a download.
    /// True: WireGuard asks to download wireguard-go first.
    /// False: WireGuard tries to run a file that is not there.
    /// </summary>
    [Fact]
    public void Find_IsNullBeforeADownload()
    {
        using var store = new SidecarStore(_folder, null, NullLogger<SidecarStore>.Instance);

        Assert.Null(store.Find());
        Assert.False(store.Info().Installed);
    }

    /// <summary>
    /// A file whose hash matches is kept with its version and may run.
    /// True: Settings shows the version and the tunnels start.
    /// False: the download is lost or cannot run.
    /// </summary>
    [Fact]
    public void Keep_MatchingHash_KeepsAnExecutableFile()
    {
        using var store = new SidecarStore(_folder, null, NullLogger<SidecarStore>.Instance);
        var bytes = new byte[] { 1, 2, 3 };

        store.Keep(bytes, Convert.ToHexString(SHA256.HashData(bytes)), "1.0.0");

        Assert.Equal(store.BinaryPath, store.Find());
        Assert.Equal("1.0.0", store.Info().Version);
        Assert.True(store.Info().Installed);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True((File.GetUnixFileMode(store.BinaryPath) & UnixFileMode.UserExecute) != 0);
        }
    }

    /// <summary>
    /// A file whose hash does not match is refused, and the one kept before stays.
    /// True: a damaged or swapped download never runs.
    /// False: the server runs a file the release did not publish.
    /// </summary>
    [Fact]
    public void Keep_WrongHash_IsRefusedAndKeepsTheOldFile()
    {
        using var store = new SidecarStore(_folder, null, NullLogger<SidecarStore>.Instance);
        var good = new byte[] { 1, 2, 3 };
        store.Keep(good, Convert.ToHexString(SHA256.HashData(good)), "1.0.0");

        Assert.Throws<InvalidOperationException>(() => store.Keep(new byte[] { 9 }, new string('0', 64), "2.0.0"));

        Assert.Equal(good, File.ReadAllBytes(store.BinaryPath));
        Assert.Equal("1.0.0", store.Info().Version);
    }

    /// <summary>
    /// A download with no way to send it says so rather than failing silently.
    /// True: the page shows why the download did not start.
    /// False: the request hangs or throws an unhandled error.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_WithoutAClient_Refuses()
    {
        using var store = new SidecarStore(_folder, null, NullLogger<SidecarStore>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DownloadAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }
}
