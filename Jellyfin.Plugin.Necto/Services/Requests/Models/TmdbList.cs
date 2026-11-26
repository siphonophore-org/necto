using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>A TMDb list of movies or shows: where it is and how it's filtered and ordered.</summary>
/// <param name="Type">Movies or shows.</param>
/// <param name="Path">The list, such as trending/tv/week or discover/movie.</param>
/// <param name="Query">Its own parameters, such as with_genres.</param>
/// <param name="Row">The request page's home row it is, such as TrendingShows, whose first pages are stored daily.</param>
public sealed record TmdbList(RequestType Type, string Path, IReadOnlyDictionary<string, string> Query, string? Row = null);
