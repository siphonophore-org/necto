using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// How requesting a whole collection went.
/// </summary>
public class CollectionRequestResult
{
    /// <summary>Gets or sets how many movies were requested.</summary>
    public int Done { get; set; }

    /// <summary>Gets or sets how many were left alone: in the library, requested or on their way already.</summary>
    public int Skipped { get; set; }

    /// <summary>Gets or sets how many were refused, by the blocklist or a limit, say.</summary>
    public int Failed { get; set; }

    /// <summary>Gets or sets why the first refused one was, to show the requester.</summary>
    public string? Error { get; set; }
}
