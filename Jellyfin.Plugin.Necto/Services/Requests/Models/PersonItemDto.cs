using MediaBrowser.Model.Dto;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A person found by search, as Jellyfin describes a person, with the person as found under <see cref="Person"/>.
/// </summary>
public class PersonItemDto : BaseItemDto
{
    /// <summary>Gets or sets the person as found: their name, role, photo and TMDb id.</summary>
    public PersonCredit? Person { get; set; }
}
