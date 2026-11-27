namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// The app a route to another service forwards to, whose published reverse proxy example its settings follow.
/// </summary>
public enum RoutePreset
{
    /// <summary>Any web app.</summary>
    Generic,

    /// <summary>Jellyseerr, Seerr, or Overseerr.</summary>
    Seerr,

    /// <summary>Ombi.</summary>
    Ombi,

    /// <summary>Radarr, Sonarr, Lidarr, Readarr, Prowlarr, or Bazarr.</summary>
    Arr,

    /// <summary>qBittorrent's web UI.</summary>
    QBittorrent,

    /// <summary>Home Assistant.</summary>
    HomeAssistant
}
