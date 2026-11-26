using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// What a Radarr or Sonarr connection test found.
/// </summary>
public class ArrOptions
{
    /// <summary>Gets or sets a value indicating whether the server answered.</summary>
    public bool Ok { get; set; }

    /// <summary>Gets or sets what went wrong, when it did not.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the server's version.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets the quality profiles, by id.</summary>
    public List<ArrProfile> Profiles { get; set; } = new();

    /// <summary>Gets or sets the root folder paths.</summary>
    public List<string> RootFolders { get; set; } = new();

    /// <summary>Gets or sets the server's tags; empty when it has none or they couldn't be read.</summary>
    public List<ArrTag> Tags { get; set; } = new();
}
