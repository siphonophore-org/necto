using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The types of media that can be requested.
/// </summary>
public enum RequestType
{
    /// <summary>A film.</summary>
    Movie = 0,

    /// <summary>A show, requested as a whole.</summary>
    Series = 1
}
