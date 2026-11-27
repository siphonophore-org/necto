using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Keeps the downloaded wgsidecar, the WireGuard sidecar built on wireguard-go, in Necto's data folder. It downloads
/// the file for this server from the release <see cref="SidecarRelease"/> names, checks it against the release's
/// SHA256SUMS, and replaces the kept file only once the new one passes.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class SidecarStore : IDisposable
{
    /// <summary>The largest sidecar the download reads.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    private readonly string _folder;
    private readonly IHttpClientFactory? _http;
    private readonly ILogger<SidecarStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="SidecarStore"/> class in Jellyfin's data folder.
    /// </summary>
    /// <param name="paths">Gives the data folder.</param>
    /// <param name="http">Sends the download.</param>
    /// <param name="logger">Logs the download.</param>
    public SidecarStore(IApplicationPaths paths, IHttpClientFactory http, ILogger<SidecarStore> logger)
        : this(Path.Combine((paths ?? throw new ArgumentNullException(nameof(paths))).DataPath, "necto", "wireguard", "bin"), http, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SidecarStore"/> class in a folder, for tests.
    /// </summary>
    /// <param name="folder">Where the sidecar is kept.</param>
    /// <param name="http">Sends the download, or null when nothing is downloaded.</param>
    /// <param name="logger">Logs the download.</param>
    internal SidecarStore(string folder, IHttpClientFactory? http, ILogger<SidecarStore> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
        _http = http;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the path the sidecar is kept at.</summary>
    public string BinaryPath => Path.Combine(_folder, OperatingSystem.IsWindows() ? "wgsidecar.exe" : "wgsidecar");

    private string VersionPath => Path.Combine(_folder, "wgsidecar.version");

    /// <summary>
    /// Finds the kept sidecar and makes sure it may run.
    /// </summary>
    /// <returns>The sidecar's path, or null until it is downloaded.</returns>
    public string? Find()
    {
        if (!File.Exists(BinaryPath))
        {
            return null;
        }

        EnsureExecutable(BinaryPath);
        return BinaryPath;
    }

    /// <summary>
    /// Describes the kept sidecar and the release Necto downloads.
    /// </summary>
    /// <returns>Whether it is downloaded, its version and when, this server's platform, and the release.</returns>
    public SidecarInfo Info()
    {
        var installed = File.Exists(BinaryPath);
        string? version = null;
        if (installed && File.Exists(VersionPath))
        {
            version = File.ReadAllText(VersionPath).Trim();
        }

        return new SidecarInfo
        {
            Installed = installed,
            Version = version,
            DownloadedUtc = installed ? File.GetLastWriteTimeUtc(BinaryPath) : null,
            Platform = SidecarRelease.CurrentPlatform(),
            ReleaseVersion = SidecarRelease.Version
        };
    }

    /// <summary>
    /// Downloads the sidecar for this server and keeps it, replacing the one kept before.
    /// </summary>
    /// <param name="cancellationToken">Stops the download.</param>
    /// <returns>The kept sidecar's description.</returns>
    /// <exception cref="InvalidOperationException">When no sidecar is built for this server, or the download fails or does not match its hash.</exception>
    public async Task<SidecarInfo> DownloadAsync(CancellationToken cancellationToken)
    {
        var platform = SidecarRelease.CurrentPlatform() ?? throw new InvalidOperationException("WireGuard has no sidecar for this server's platform.");
        if (_http is null)
        {
            throw new InvalidOperationException("WireGuard cannot download its sidecar here.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = SidecarRelease.FileName(platform);
            var baseUri = SidecarRelease.BaseUri(SidecarRelease.Version);
            var sums = System.Text.Encoding.UTF8.GetString(await GetAsync(new Uri(baseUri, "SHA256SUMS"), 1024 * 1024, cancellationToken).ConfigureAwait(false));
            var expected = SidecarRelease.HashOf(sums, file) ?? throw new InvalidOperationException("The sidecar release does not list a file for this server.");
            var bytes = await GetAsync(new Uri(baseUri, file), MaxBytes, cancellationToken).ConfigureAwait(false);
            Keep(bytes, expected, SidecarRelease.Version);
            _logger.LogInformation("Downloaded wgsidecar {Version} for {Platform}", SidecarRelease.Version, platform);
            return Info();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Keeps a downloaded sidecar once its hash matches, replacing the one kept before.
    /// </summary>
    /// <param name="bytes">The downloaded file.</param>
    /// <param name="expectedHash">Its SHA-256 from the release, in hex.</param>
    /// <param name="version">The release it came from.</param>
    /// <exception cref="InvalidOperationException">When the hash does not match.</exception>
    public void Keep(byte[] bytes, string expectedHash, string version)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrEmpty(expectedHash);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!SecretCompare.FixedTimeEquals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The downloaded sidecar does not match its release hash, so it was not kept.");
        }

        Directory.CreateDirectory(_folder);
        AtomicFile.WriteAllBytes(BinaryPath, bytes, ownerOnly: true, logger: _logger);
        EnsureExecutable(BinaryPath);
        AtomicFile.WriteAllBytes(VersionPath, System.Text.Encoding.UTF8.GetBytes(version), logger: _logger);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    // Reads one release file, refusing anything larger than the cap.
    private async Task<byte[]> GetAsync(Uri uri, long max, CancellationToken cancellationToken)
    {
        var client = _http!.CreateClient();
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach the wgsidecar release at {Uri}", uri);
            throw new InvalidOperationException("Could not reach the sidecar release. Check the server's internet connection.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The wgsidecar release answered {Status} for {Uri}", (int)response.StatusCode, uri);
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "The sidecar release answered {0}.", (int)response.StatusCode));
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var copy = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (copy.Length + read > max)
                    {
                        throw new InvalidOperationException("The sidecar release file is larger than expected, so it was not kept.");
                    }

                    await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                return copy.ToArray();
            }
        }
    }

    // A copied or restored file may lose its executable bit, so it is set again before every start.
    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        if ((mode & UnixFileMode.UserExecute) == 0)
        {
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute);
        }
    }
}
