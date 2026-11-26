using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A profile a requester may pick, by name only: which server it is on stays with the administrators.
/// </summary>
/// <param name="Id">The profile's id.</param>
/// <param name="Name">The profile's name, such as 1080p or 4K.</param>
public sealed record ProfileOption(string Id, string Name);
