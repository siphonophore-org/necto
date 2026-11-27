using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// Where an administrator opens a request's title in Radarr or Sonarr.
/// </summary>
/// <param name="Url">The title's page there, at the server's browser address.</param>
/// <param name="Server">The server's name, such as Radarr or 4K Radarr.</param>
public record ArrLink(string Url, string Server);
