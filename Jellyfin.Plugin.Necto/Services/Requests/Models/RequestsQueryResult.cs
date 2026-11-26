using System.Collections.Generic;
using MediaBrowser.Model.Querying;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// A page of a list, as Jellyfin returns one: its items, how many there are in all, and where the page starts.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class RequestsQueryResult<T> : QueryResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestsQueryResult{T}"/> class.
    /// </summary>
    /// <param name="startIndex">Where the page starts.</param>
    /// <param name="totalRecordCount">How many there are in all.</param>
    /// <param name="items">The page's items.</param>
    public RequestsQueryResult(int startIndex, int totalRecordCount, IReadOnlyList<T> items)
        : base(startIndex, totalRecordCount, items)
    {
    }

    /// <summary>Gets how many there are in all, under the name the dashboard's table reads; the same as TotalRecordCount.</summary>
    public int TotalCount => TotalRecordCount;

    /// <summary>
    /// Gets or sets where the next page starts, for a list that skips some of its titles (those the blocklist holds back)
    /// so a page's count can't say; null at the list's end. Absent from lists that skip none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? NextStartIndex { get; set; }
}
