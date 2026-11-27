namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// The forwarding profile of a route to Jellyfin, after Jellyfin's own reverse proxy guide.
/// </summary>
public enum JellyfinProfile
{
    /// <summary>Jellyfin's documented settings, with the limits in Settings.</summary>
    Recommended,

    /// <summary>Fewer connections, smaller uploads, and shorter timeouts, for a small server.</summary>
    LowMemory,

    /// <summary>Long timeouts and large uploads, for streaming far from home.</summary>
    RemoteStreaming,

    /// <summary>The route's own values.</summary>
    Custom
}
