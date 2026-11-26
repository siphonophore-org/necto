using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One of the empty search's rows, its titles as Jellyfin items.
/// </summary>
/// <param name="Key">The row's key, such as TrendingShows or UpcomingMovies.</param>
/// <param name="Title">The row's heading.</param>
/// <param name="Items">The row's titles: the start of its list, read on with Requests/Discover/{key}.</param>
public record DiscoverRowDto(string Key, string Title, IReadOnlyList<SearchItemDto> Items);
