namespace Jellyfin.Plugin.Necto.Api.CustomPages;

/// <summary>
/// The answer of <c>POST /Pages/Stores/{name}/Clear</c>.
/// </summary>
public class StoreClearResponse
{
    /// <summary>Gets or sets how many records were deleted.</summary>
    public int Removed { get; set; }
}
