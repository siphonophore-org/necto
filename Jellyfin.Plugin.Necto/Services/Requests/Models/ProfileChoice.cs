using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A profile a request can be sent with, for choosing one on approval.
/// </summary>
/// <param name="Id">The profile's id.</param>
/// <param name="Name">The profile's name, such as 1080p or 4K.</param>
/// <param name="ServerId">The server it is on.</param>
/// <param name="Server">The server's name, such as Radarr or 4K Radarr.</param>
/// <param name="Type">What the server takes.</param>
public record ProfileChoice(string Id, string Name, string ServerId, string Server, RequestType Type);
