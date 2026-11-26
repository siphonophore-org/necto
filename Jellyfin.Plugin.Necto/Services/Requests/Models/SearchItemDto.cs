using MediaBrowser.Model.Dto;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A title found by search, or in the empty search's rows, as Jellyfin describes an item: the library's own item once
/// the title is there, otherwise the title as found. Where it stands for requesting rides along under
/// <see cref="Result"/>, which apps that only know <see cref="BaseItemDto"/> leave alone.
/// </summary>
public class SearchItemDto : BaseItemDto
{
    /// <summary>Gets or sets the title as the request page uses it: whether it's requested, blocked, on its way.</summary>
    public SearchResult? Result { get; set; }
}
