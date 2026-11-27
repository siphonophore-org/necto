using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// The settings each preset fills in, after the reverse proxy examples each app publishes. Every preset passes
/// WebSockets on, never buffers, and keeps the visitor's host name unless the app needs its own.
/// </summary>
public static class RoutePresets
{
    /// <summary>
    /// Builds the settings a preset fills in.
    /// </summary>
    /// <param name="preset">The preset.</param>
    /// <returns>New settings for the preset.</returns>
    public static RouteOptions For(RoutePreset preset)
    {
        var options = new RouteOptions { Preset = preset };
        switch (preset)
        {
            case RoutePreset.Seerr:
            case RoutePreset.Ombi:
                // Avatars and settings imports are small.
                options.MaxBodyMb = 20;
                break;
            case RoutePreset.Arr:
                // Manual imports and backups can take minutes to answer and restore from an upload.
                options.TimeoutSeconds = 300;
                options.MaxBodyMb = 500;
                break;
            case RoutePreset.QBittorrent:
                // The web UI refuses a Host that is not its own, and takes .torrent files.
                options.PreserveHost = false;
                options.MaxBodyMb = 50;
                break;
            case RoutePreset.HomeAssistant:
                // The frontend holds one WebSocket open for hours, and backups are uploaded whole.
                options.TimeoutSeconds = 3600;
                options.MaxBodyMb = 0;
                break;
        }

        return options;
    }

    /// <summary>
    /// The request headers a route may never set, since the proxy sets them itself or they control the connection.
    /// </summary>
    public static IReadOnlySet<string> ReservedRequestHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Upgrade", "Keep-Alive", "Transfer-Encoding", "Content-Length", "TE", "Trailer", "Proxy-Connection",
        "Forwarded", "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "X-Real-IP", "X-Forwarded-Prefix",
        "X-Forwarded-Port", "X-Forwarded-Ssl", "X-Forwarded-Scheme", "X-Forwarded-Server", "X-Forwarded-Protocol",
        "X-Original-URL", "X-Rewrite-URL", "X-Original-Forwarded-For", "X-Client-IP", "X-Cluster-Client-IP",
        "True-Client-IP", "CF-Connecting-IP", "Fastly-Client-IP", "X-ProxyUser-Ip"
    };

    /// <summary>
    /// The answer headers a route may never set, since they control the connection or the proxy's own security headers.
    /// </summary>
    public static IReadOnlySet<string> ReservedResponseHeaders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Upgrade", "Keep-Alive", "Transfer-Encoding", "Content-Length", "Trailer", "Strict-Transport-Security"
    };
}
