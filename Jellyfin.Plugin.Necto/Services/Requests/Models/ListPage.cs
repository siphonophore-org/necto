using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>A page read from a list that may skip titles: the titles, how many the list holds, and where to read on.</summary>
/// <param name="Items">The titles.</param>
/// <param name="Total">How many titles the list holds, before any are skipped.</param>
/// <param name="Next">Where the next page starts, in the list's own order; null at its end.</param>
public sealed record ListPage(List<SearchResult> Items, int Total, int? Next);
