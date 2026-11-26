using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Services.CustomPages;

namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// The answer of <c>GET /pages/store/{name}/read</c>.
/// </summary>
public class StoreReadResponse
{
    /// <summary>Gets or sets the records the caller may see, newest first.</summary>
    public IReadOnlyList<StoreRecord> Records { get; set; } = new List<StoreRecord>();

    /// <summary>Gets or sets how many records came back.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets how many records the store takes before it refuses writes.</summary>
    public int Limit { get; set; }

    /// <summary>Gets or sets what the caller was shown, <c>all</c> for every record or <c>own</c> for only theirs.</summary>
    public string Scope { get; set; } = "own";
}
