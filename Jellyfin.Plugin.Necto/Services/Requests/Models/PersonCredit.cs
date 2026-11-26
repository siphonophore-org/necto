using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// One person in a title's cast or crew.
/// </summary>
public class PersonCredit
{
    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the character played, or the job, such as Director.</summary>
    public string? Role { get; set; }

    /// <summary>Gets or sets a photo address, when known.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Gets or sets the person's TMDb id, when known.</summary>
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets Jellyfin's own id for the person when the library has them in something the viewer may see, so the
    /// page opens their Jellyfin page; null otherwise.
    /// </summary>
    public System.Guid? LibraryPersonId { get; set; }
}
