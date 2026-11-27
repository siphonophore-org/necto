namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// The answer of <c>GET /Pages/Stores/{name}/Stats</c>, for the Custom pages tool.
/// </summary>
public class StoreStatsResponse
{
    /// <summary>Gets or sets how many records the store holds.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets how many records the store takes before it refuses writes.</summary>
    public int Limit { get; set; }
}
