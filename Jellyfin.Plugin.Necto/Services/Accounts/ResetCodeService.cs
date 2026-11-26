using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Necto.Services.Accounts;

/// <summary>
/// Reads the password reset codes Jellyfin's own reset provider writes as <c>passwordreset*.json</c> in the program data
/// folder, which Jellyfin has no route to read back. A file that does not parse is skipped.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public sealed class ResetCodeService
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly ILogger<ResetCodeService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetCodeService"/> class.
    /// </summary>
    /// <param name="paths">Gives the program data folder the reset files are in.</param>
    /// <param name="logger">Writes the server log.</param>
    /// <param name="time">Tells whether a code has expired. Left out, it is the system clock.</param>
    public ResetCodeService(IApplicationPaths paths, ILogger<ResetCodeService> logger, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = paths.ProgramDataPath;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads every pending reset, the latest expiry first.
    /// </summary>
    /// <returns>The resets, empty when there are none or the folder cannot be read.</returns>
    public IReadOnlyList<ResetCode> ReadAll()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var results = new List<ResetCode>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "passwordreset*.json"))
            {
                if (ReadOne(file, now) is { } entry)
                {
                    results.Add(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list the password reset files in {Directory}", _directory);
        }

        return results.OrderByDescending(r => r.ExpirationDate).ToList();
    }

    private ResetCode? ReadOne(string file, DateTime now)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var data = JsonSerializer.Deserialize<ResetFile>(stream, Options);
            if (data is null || string.IsNullOrEmpty(data.Pin))
            {
                return null;
            }

            return new ResetCode
            {
                UserName = data.UserName ?? string.Empty,
                Pin = data.Pin,
                ExpirationDate = data.ExpirationDate,
                Expired = data.ExpirationDate < now
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // The file name stays out of the log, so nothing read from a reset file reaches it.
            _logger.LogWarning(ex, "Could not read a password reset file in {Directory}", _directory);
            return null;
        }
    }

    /// <summary>
    /// The shape Jellyfin's own reset provider writes. Unknown members are ignored.
    /// </summary>
    private sealed class ResetFile
    {
        public string? Pin { get; set; }

        public string? UserName { get; set; }

        public DateTime ExpirationDate { get; set; }
    }
}
