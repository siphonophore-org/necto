using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// TMDb's posters, backdrops and photos, kept in the server's cache folder (cache/requests/images) the first time
/// they're shown, so the page loads them from the server rather than from TMDb each time. TMDb's image addresses never
/// change what they show, so a kept image is good for as long as it's kept. The daily refresh keeps the home rows'
/// posters ahead of time and lets go of the rest, so what's kept besides is what was shown since. Images unused for a
/// month go, and the whole stays under a size limit, when the cleanup task runs, and as soon as the limit is reached.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Paths are built only from a size in a fixed set and a file name held to letters, digits, - and _ with an image extension (Source).")]
[PluginService(ServiceLifetime.Singleton)]
public sealed partial class ArtworkCache : IDisposable
{
    private const string Origin = "https://image.tmdb.org/t/p/";

    // The sizes the plugin asks TMDb for: never its originals, which are many megabytes and nothing here shows.
    private static readonly HashSet<string> _sizes = new(StringComparer.Ordinal) { "w92", "w154", "w185", "w300", "w342", "w500", "w780", "w1280", "h632" };

    // How many images are read from TMDb at once; past that, one is sent on to TMDb's own address.
    private const int MaxFetching = 8;

    // The most one image may be, and the most all of them may come to.
    private const long MaxImageBytes = 10L * 1024 * 1024;
    private const long MaxTotalBytes = 1024L * 1024 * 1024;

    // How long an image is kept without being shown, and how often showing one marks it as used.
    private static readonly TimeSpan _unusedFor = TimeSpan.FromDays(30);
    private static readonly TimeSpan _markEvery = TimeSpan.FromDays(1);

    // The longest reading one image from TMDb may take, headers and body together.
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerApplicationPaths _paths;
    private readonly ILogger<ArtworkCache> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _fetching = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _fetchGate = new(MaxFetching, MaxFetching);

    // What the folder holds, in bytes: counted once in the background, then kept as images are added and let go.
    // Until it's been counted the folder isn't taken to be full.
    private long _bytes;
    private int _counted;
    private readonly object _countLock = new();

    // 1 while a cleanup started for reaching the limit runs, so only one does, and when the last one started, so one
    // that couldn't make room (a folder it may not delete from) isn't started again on every image; one cleanup at a
    // time of any kind.
    private int _trimming;
    private long _trimStartedTicks;
    private readonly object _trimLock = new();
    private static readonly TimeSpan _trimEvery = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtworkCache"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="paths">The server's paths, for its cache folder.</param>
    /// <param name="logger">The logger.</param>
    public ArtworkCache(IHttpClientFactory httpClientFactory, IServerApplicationPaths paths, ILogger<ArtworkCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _logger = logger;

        // Counted off the request threads, once: walking a folder of thousands of images takes a while.
        _ = Task.Run(Count);
    }

    /// <inheritdoc />
    public void Dispose() => _fetchGate.Dispose();

    /// <summary>Gets the folder the images are kept in.</summary>
    public string Folder => Path.Combine(_paths.CachePath, "requests", "images");

    /// <summary>Returns TMDb's address for an image, when the size and file are ones it serves.</summary>
    /// <param name="size">The size, such as w342.</param>
    /// <param name="file">The file, such as abc123.jpg.</param>
    /// <returns>The address, or null.</returns>
    public static string? Source(string size, string file)
        => _sizes.Contains(size) && FileName().IsMatch(file) ? Origin + size + "/" + file : null;

    /// <summary>
    /// Gets an image's file on the server, read from TMDb the first time it's asked for. Asked for by several at once,
    /// it's read once.
    /// </summary>
    /// <param name="size">The size, such as w342.</param>
    /// <param name="file">The file, such as abc123.jpg.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file's path; null when the image isn't one TMDb serves or couldn't be read.</returns>
    public async Task<string?> GetAsync(string size, string file, CancellationToken cancellationToken)
    {
        if (Source(size, file) is not { } source)
        {
            return null;
        }

        // Source has held the size to TMDb's few and the file to letters, digits, - and _ with an image extension, so
        // neither can name anything outside the folder.
        var path = Path.Combine(Folder, size, file);
        if (File.Exists(path))
        {
            MarkUsed(path);
            return path;
        }

        // Already being read: waited for, taking no place of its own.
        var key = size + "/" + file;
        if (_fetching.TryGetValue(key, out var reading))
        {
            return await reading.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Full, or busy: sent on to TMDb rather than kept, so the folder never outgrows its limit and no one can have
        // the server fetch without end. Full starts a cleanup, which makes room again soon rather than at the next
        // nightly one. The place is held by the read, not by whoever asked, so asking and giving up again and again
        // never starts more than MaxFetching reads.
        if (Full())
        {
            TrimSoon();
            return null;
        }

        if (!_fetchGate.Wait(0, CancellationToken.None))
        {
            return null;
        }

        var mine = new Lazy<Task<string?>>(() => FetchAsync(key, source, path));
        var fetching = _fetching.GetOrAdd(key, mine);
        if (!ReferenceEquals(fetching, mine))
        {
            // Another asker started it a moment ago, holding a place of its own.
            ReleaseGate();
        }

        return await fetching.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ReleaseGate()
    {
        try
        {
            _fetchGate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    // Read whoever stops waiting, so it's kept for the next time. Written beside its place and moved in, so a half
    // written file is never served. An image past the limit stops the copy, and its part file is deleted.
    private async Task<string?> FetchAsync(string key, string source, string path)
    {
        var partial = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            // One limit for the whole read: the client's own timeout ends once the headers arrive, and an image that
            // stalls partway through mustn't hold its place for good.
            using var timeout = new CancellationTokenSource(FetchTimeout);
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            using var response = await client.GetAsync(new Uri(source), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.Ordinal) != true
                || response.Content.Headers.ContentLength > MaxImageBytes)
            {
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using (file.ConfigureAwait(false))
                {
                    await new CappedReadStream(stream, MaxImageBytes).CopyToAsync(file, 81920, timeout.Token).ConfigureAwait(false);
                }
            }

            File.Move(partial, path, overwrite: true);
            Interlocked.Add(ref _bytes, new FileInfo(path).Length);
            if (Full())
            {
                TrimSoon();
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Keeping TMDb's image {Image} failed", key);
            return null;
        }
        finally
        {
            _fetching.TryRemove(key, out _);
            TryDelete(partial);
            ReleaseGate();
        }
    }

    /// <summary>
    /// Lets go of images unused for a month, and then the least recently used until the whole is well under its limit,
    /// along with any left half written.
    /// </summary>
    /// <returns>How many files were removed.</returns>
    public int Trim()
    {
        lock (_trimLock)
        {
            return TrimOnce();
        }
    }

    /// <summary>
    /// Lets go of every image kept but those named, along with any left half written: what the daily refresh does once
    /// it has kept the home rows' posters, so the rest are only those shown since.
    /// </summary>
    /// <param name="keep">The images to keep, each as its size and file, such as w342/abc123.jpg.</param>
    /// <returns>How many files were removed.</returns>
    public int Clear(IReadOnlySet<string> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        lock (_trimLock)
        {
            if (!Directory.Exists(Folder))
            {
                Settle(0);
                return 0;
            }

            var removed = 0;
            try
            {
                long total = 0;
                var now = DateTime.UtcNow;
                foreach (var file in new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList())
                {
                    // One being read now is minutes old at most, and kept.
                    var stays = file.Name.EndsWith(".part", StringComparison.Ordinal)
                        ? now - file.LastWriteTimeUtc <= TimeSpan.FromHours(1)
                        : keep.Contains(file.Directory?.Name + "/" + file.Name);
                    if (stays || !TryDelete(file.FullName))
                    {
                        total += file.Length;
                    }
                    else
                    {
                        removed++;
                    }
                }

                Settle(total);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Clearing out kept TMDb images failed");
            }

            return removed;
        }
    }

    // Under _trimLock.
    private int TrimOnce()
    {
        if (!Directory.Exists(Folder))
        {
            Settle(0);
            return 0;
        }

        var removed = 0;
        try
        {
            var now = DateTime.UtcNow;
            var files = new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            var kept = new List<FileInfo>();
            foreach (var file in files)
            {
                var stale = file.Name.EndsWith(".part", StringComparison.Ordinal)
                    ? now - file.LastWriteTimeUtc > TimeSpan.FromHours(1)
                    : now - file.LastWriteTimeUtc > _unusedFor;
                if (stale)
                {
                    removed += TryDelete(file.FullName) ? 1 : 0;
                }
                else
                {
                    kept.Add(file);
                }
            }

            var total = kept.Sum(f => f.Length);
            Settle(total);
            if (total > MaxTotalBytes)
            {
                foreach (var file in kept.OrderBy(f => f.LastWriteTimeUtc))
                {
                    if (total <= MaxTotalBytes * 8 / 10)
                    {
                        break;
                    }

                    if (TryDelete(file.FullName))
                    {
                        total -= file.Length;
                        Interlocked.Add(ref _bytes, -file.Length);
                        removed++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Clearing out kept TMDb images failed");
        }

        return removed;
    }

    // Whether the folder has reached its limit, as far as is known: not until it's been counted.
    private bool Full() => Volatile.Read(ref _counted) == 1 && Interlocked.Read(ref _bytes) >= MaxTotalBytes;

    // Starts a cleanup in the background, unless one started this way is running already or did a moment ago.
    private void TrimSoon()
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref _trimStartedTicks) < _trimEvery.Ticks || Interlocked.CompareExchange(ref _trimming, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _trimStartedTicks, now);

        _ = Task.Run(() =>
        {
            try
            {
                Trim();
            }
            finally
            {
                Volatile.Write(ref _trimming, 0);
            }
        });
    }

    // What the folder holds, counted once. Images kept while it's counted were added to what's known as they came, so
    // the count is added to them (one kept mid-count may be counted twice, which only brings the next cleanup sooner).
    private void Count()
    {
        long total = 0;
        try
        {
            if (Directory.Exists(Folder))
            {
                total = new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Counted as empty; the cleanup counts it again.
        }

        lock (_countLock)
        {
            if (_counted == 0)
            {
                Interlocked.Add(ref _bytes, total);
                Volatile.Write(ref _counted, 1);
            }
        }

        if (Full())
        {
            TrimSoon();
        }
    }

    // What the folder holds, as a cleanup has just counted it: exact, whether or not it had been counted before.
    private void Settle(long total)
    {
        lock (_countLock)
        {
            Interlocked.Exchange(ref _bytes, total);
            Volatile.Write(ref _counted, 1);
        }
    }

    // Shown again: marked as used, at most once a day, so the cleanup keeps it.
    private static void MarkUsed(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - File.GetLastWriteTimeUtc(path) > _markEvery)
            {
                File.SetLastWriteTimeUtc(path, now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept as it is; the cleanup may let it go a little early.
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next cleanup.
        }

        return false;
    }

    // Bitmaps only: an SVG served from the server's own address could run script when opened on its own.
    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\.(jpg|jpeg|png|webp)$")]
    private static partial System.Text.RegularExpressions.Regex FileName();
}
