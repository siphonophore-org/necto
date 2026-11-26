using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Necto.Services.Requests;

/// <summary>
/// Tells Jellyfin about files Radarr or Sonarr has just put in place, so a request turns available without waiting
/// for the next library scan. As the web API's media-updated call, a title's folder is handed to the library monitor,
/// which refreshes just that folder once it settles. A folder outside every library (Radarr or Sonarr seeing the media
/// at another path) can't be found that way, so a full scan is asked for instead, as a missed change costs more; but
/// at most once an hour, since such a setup would otherwise scan every library on every sync with new files.
/// </summary>
[PluginService(ServiceLifetime.Singleton)]
public class LibraryRefresher
{
    /// <summary>The shortest time between two full scans asked for because a folder was outside every library.</summary>
    internal static readonly TimeSpan FullScanEvery = TimeSpan.FromHours(1);

    private readonly object _lock = new();
    private DateTime? _lastFullScanUtc;

    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<LibraryRefresher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryRefresher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="libraryMonitor">The library monitor.</param>
    /// <param name="logger">The logger.</param>
    public LibraryRefresher(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, ILogger<LibraryRefresher> logger)
    {
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes the library for titles with new files, as the Refresh on available setting says.
    /// </summary>
    /// <param name="folders">Each title's folder as Radarr or Sonarr reported it, or null when it didn't say.</param>
    public void Refresh(IReadOnlyCollection<string?> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var mode = RequestConfig.Instance?.ReadConfiguration(c => c.RefreshOnAvailable) ?? LibraryRefresh.ItemFolder;

        // A title whose folder wasn't said is left to the library's own scans: there's nothing to point at, and no
        // reason to think it's outside every library.
        var said = Said(folders);
        if (said.Count == 0 || mode == LibraryRefresh.Disabled)
        {
            return;
        }

        try
        {
            if (mode == LibraryRefresh.ItemFolder)
            {
                var (inside, outside) = Sort(said, LibraryRoots());
                foreach (var folder in inside)
                {
                    _logger.LogDebug("Refreshing {Folder} for its new files", folder);
                    _libraryMonitor.ReportFileSystemChanged(folder);
                }

                if (!outside)
                {
                    return;
                }

                bool due;
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    due = FullScanDue(_lastFullScanUtc, now);
                    if (due)
                    {
                        _lastFullScanUtc = now;
                    }
                }

                if (!due)
                {
                    _logger.LogDebug("A title with new files is not inside any library as Radarr or Sonarr sees it; every library was scanned for that within the hour, so the next scan will find it");
                    return;
                }

                _logger.LogInformation("A title with new files is not inside any library as Radarr or Sonarr sees it, so every library is scanned instead");
            }
            else
            {
                _logger.LogInformation("Scanning every library for {Count} title(s) with new files", said.Count);
            }

            _libraryManager.QueueLibraryScan();
        }
        catch (Exception ex)
        {
            // The library's own scans still find the files.
            _logger.LogWarning(ex, "Asking for a library refresh failed");
        }
    }

    // The folders Radarr or Sonarr said, without the titles it didn't say one for.
    internal static List<string> Said(IEnumerable<string?> folders)
        => folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).ToList();

    // The folders inside a library, each once (two requests for one show share its folder), and whether any is outside every library.
    internal static (List<string> Inside, bool Outside) Sort(IReadOnlyCollection<string> folders, IReadOnlyCollection<string> roots)
        => (folders.Where(f => Within(f, roots)).Distinct(PathComparer).ToList(), folders.Any(f => !Within(f, roots)));

    // Whether a full scan for a folder outside every library may be asked for now, the last one being at last.
    internal static bool FullScanDue(DateTime? last, DateTime now) => last is not { } at || now - at >= FullScanEvery;

    // Every folder the libraries are made of, each ending in a separator so /movies doesn't take in /movies-4k.
    private List<string> LibraryRoots() => _libraryManager.GetVirtualFolders()
        .SelectMany(v => v.Locations ?? Array.Empty<string>())
        .Where(l => !string.IsNullOrWhiteSpace(l))
        .Select(l => Path.TrimEndingDirectorySeparator(l) + Path.DirectorySeparatorChar)
        .ToList();

    // Paths compare as the file system does: by case only on Linux.
    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    internal static bool Within(string? folder, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return false;
        }

        // A path that steps out of where it says (/media/movies/../../etc) isn't taken at its word; a name that merely
        // has dots in it (Vol..2) is fine.
        var path = Path.TrimEndingDirectorySeparator(folder);
        if (!string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
        {
            return false;
        }

        path += Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return roots.Any(r => path.StartsWith(r, comparison) && path.Length > r.Length);
    }
}
