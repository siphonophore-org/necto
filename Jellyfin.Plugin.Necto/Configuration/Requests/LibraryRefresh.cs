using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Requests.Models;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Configuration.Requests;

/// <summary>
/// What Jellyfin is asked to scan when Radarr or Sonarr has new files for a request.
/// </summary>
public enum LibraryRefresh
{
    /// <summary>Just the title's own folder, as Jellyfin's real time monitor would.</summary>
    ItemFolder = 0,

    /// <summary>Every library, as the Scan All Libraries task.</summary>
    FullLibrary = 1,

    /// <summary>Nothing; the library's own scans find the files.</summary>
    Disabled = 2
}
