using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <content>
/// TMDb's answers in the server's cache folder (cache/requests/tmdb), one file each, so what was kept is still there
/// after a restart, and what memory let go of can be read again without asking TMDb. A file is named by a hash of what
/// was asked and holds the answer as TMDb gave it; when it was written is when the answer was kept. No key and nothing
/// about users is in any of it.
/// </content>
public sealed partial class TmdbDiscovery
{
    /// <summary>The most bytes of TMDb's answers kept in the cache folder; past it, answers are kept in memory only until the next refresh.</summary>
    internal const long MaxKeptOnDisk = 512L * 1024 * 1024;

    // What the folder holds, in bytes: counted on start and after each refresh, then added to as answers are kept.
    private long _onDisk;
    private int _warnedKeeping;

    // Where the answers are kept: Jellyfin's cache folder, as they're TMDb's to give again whenever they're lost. None
    // when the server has no cache folder to give (in tests), and then they're kept in memory only.
    private string? KeptFolder
        => (_serverConfig.ApplicationPaths as MediaBrowser.Common.Configuration.IApplicationPaths)?.CachePath is { Length: > 0 } cache
            ? Path.Combine(cache, "requests", "tmdb")
            : null;

    private static string FileOf(string folder, string key)
        => Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");

    /// <summary>
    /// Makes the answers kept in the cache folder ready on start: those kept too long ago to use go, the rest are
    /// counted, and the rows' stored copy of versions before this one goes too.
    /// </summary>
    public void LoadStore()
    {
        if (KeptFolder is not { } folder)
        {
            return;
        }

        try
        {
            // Before every answer was kept here, the home rows alone were, in one file.
            File.Delete(Path.Combine(Path.GetDirectoryName(folder)!, "discovery.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left where it is: nothing reads it.
        }

        ClearKept(DateTime.UtcNow - KeptFor);
    }

    // An answer from the cache folder, when it's there and not kept too long ago to use.
    private (JsonElement Body, long Bytes, DateTime SavedUtc)? ReadKept(string key)
    {
        if (KeptFolder is not { } folder)
        {
            return null;
        }

        try
        {
            var file = new FileInfo(FileOf(folder, key));
            if (!file.Exists || DateTime.UtcNow - file.LastWriteTimeUtc >= KeptFor)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(file.FullName);
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.ValueKind == JsonValueKind.Object ? (document.RootElement.Clone(), bytes.Length, file.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable, or let go of as it was read: asked of TMDb instead.
            return null;
        }
    }

    // Writes an answer to the cache folder, whole and then moved into place, so one half written is never read. One
    // that can't be written (a folder that can't be written to, a full disk, the limit reached) is kept in memory only.
    private async Task WriteKeptAsync(string key, byte[] bytes)
    {
        if (KeptFolder is not { } folder || Interlocked.Read(ref _onDisk) + bytes.Length > MaxKeptOnDisk)
        {
            return;
        }

        var path = FileOf(folder, key);
        var partial = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(partial, bytes).ConfigureAwait(false);
            var before = new FileInfo(path) is { Exists: true } was ? was.Length : 0;
            File.Move(partial, path, overwrite: true);
            Interlocked.Add(ref _onDisk, bytes.Length - before);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Interlocked.Exchange(ref _warnedKeeping, 1) == 0)
            {
                _logger.LogWarning(ex, "TMDb's answers could not be written to the cache folder; they are kept in memory until the server restarts");
            }

            try
            {
                File.Delete(partial);
            }
            catch (Exception gone) when (gone is IOException or UnauthorizedAccessException)
            {
                // Nothing more to do.
            }
        }
    }

    // Lets go of the answers in the cache folder kept before a time, and any left half written, and counts the rest.
    private int ClearKept(DateTime beforeUtc)
    {
        if (KeptFolder is not { } folder || !Directory.Exists(folder))
        {
            return 0;
        }

        var gone = 0;
        long left = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                try
                {
                    // A part still being written is seconds old; an older one was left by a write that never finished.
                    var isPart = file.Extension == ".part";
                    if (isPart ? DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromMinutes(5) : file.LastWriteTimeUtc < beforeUtc)
                    {
                        file.Delete();
                        gone += isPart ? 0 : 1;
                    }
                    else if (!isPart)
                    {
                        left += file.Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone already, or not ours to delete: left for the next refresh.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Clearing TMDb's kept answers failed");
        }

        Interlocked.Exchange(ref _onDisk, left);
        return gone;
    }

    /// <summary>Gets about how many bytes of TMDb's answers are kept in the cache folder.</summary>
    internal long KeptOnDisk => Interlocked.Read(ref _onDisk);
}
