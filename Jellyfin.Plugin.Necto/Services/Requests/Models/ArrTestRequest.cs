using Jellyfin.Plugin.Necto.Configuration.Requests;
using Jellyfin.Plugin.Necto.Configuration.Issues;
using Jellyfin.Plugin.Necto.Services.Requests;
using Jellyfin.Plugin.Necto.Services.Issues;
using Jellyfin.Plugin.Necto.Services.Issues.Models;

namespace Jellyfin.Plugin.Necto.Services.Requests.Models;

/// <summary>
/// The Radarr or Sonarr connection the settings page asks the plugin to try.
/// </summary>
public class ArrTestRequest
{
    /// <summary>Gets or sets the address.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets the saved server being tested, whose stored key the kept sentinel stands for.</summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the API key, or the kept sentinel to use the stored one.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
