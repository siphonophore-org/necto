using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// How a route to another service forwards: its preset and each setting the preset fills in. A route to Jellyfin
/// ignores these and always uses Jellyfin's recommended settings. The defaults are the proxy's own behaviour.
/// </summary>
public class RouteOptions
{
    /// <summary>Gets or sets the app the settings started from.</summary>
    public RoutePreset Preset { get; set; } = RoutePreset.Generic;

    /// <summary>Gets or sets a value indicating whether WebSocket connections are passed on.</summary>
    public bool WebSockets { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the proxy may buffer the service's answers before sending them on.</summary>
    public bool Buffering { get; set; }

    /// <summary>Gets or sets the largest upload in MB, 0 for no limit, or <c>null</c> for the limit in Settings.</summary>
    public int? MaxBodyMb { get; set; }

    /// <summary>Gets or sets how long the service may stay silent in seconds, or <c>null</c> for the timeout in Settings.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Gets or sets a value indicating whether the service sees the host name the visitor asked for, rather than its own address.</summary>
    public bool PreserveHost { get; set; } = true;

    /// <summary>Gets or sets a path put in front of every forwarded path, such as <c>/radarr</c>, or empty for none.</summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>Gets or sets the headers set on every forwarded request.</summary>
    public List<HeaderEntry> RequestHeaders { get; set; } = new();

    /// <summary>Gets or sets the headers set on every answer.</summary>
    public List<HeaderEntry> ResponseHeaders { get; set; } = new();
}
