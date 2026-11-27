using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// The settings each Jellyfin profile forwards with, after Jellyfin's reverse proxy guide, and the headers no route
/// may set. Every profile passes WebSockets on, never buffers, and keeps the visitor's host name, as the guide does.
/// </summary>
public static class RouteProfiles
{
    /// <summary>How long a media stream may sit idle under <see cref="JellyfinProfile.Recommended"/>, in minutes.</summary>
    public const int MediaTimeoutMinutes = 360;

    /// <summary>The most connections the proxy opens to Jellyfin under <see cref="JellyfinProfile.Recommended"/>.</summary>
    public const int BackendConnections = 256;

    /// <summary>
    /// Builds the settings a Jellyfin profile forwards with. <see cref="JellyfinProfile.Custom"/> starts from
    /// <see cref="JellyfinProfile.Recommended"/>.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>New settings for the profile.</returns>
    public static RouteOptions For(JellyfinProfile profile)
    {
        var options = new RouteOptions
        {
            Profile = profile,
            MediaTimeoutMinutes = MediaTimeoutMinutes,
            BackendConnections = BackendConnections
        };
        switch (profile)
        {
            case JellyfinProfile.LowMemory:
                // The guide's 20 MB upload, fewer open connections, and idle requests and streams let go sooner.
                options.MaxBodyMb = 20;
                options.TimeoutSeconds = 120;
                options.MediaTimeoutMinutes = 60;
                options.BackendConnections = 32;
                break;
            case JellyfinProfile.HighBandwidth:
                // Slow links stall longer, a paused film may sit for hours, and large files go up whole.
                options.MaxBodyMb = 2048;
                options.TimeoutSeconds = 900;
                options.MediaTimeoutMinutes = 720;
                options.BackendConnections = 512;
                break;
        }

        return options;
    }

    /// <summary>
    /// Works out what a route to Jellyfin forwards with: its profile's settings, or under
    /// <see cref="JellyfinProfile.Custom"/> its own upload limit, timeouts, and connections. WebSockets, no
    /// buffering, and the visitor's host name always hold, and no path prefix or header is added.
    /// </summary>
    /// <param name="stored">The route's stored settings, or <c>null</c> for <see cref="JellyfinProfile.Recommended"/>.</param>
    /// <returns>New settings for the route.</returns>
    public static RouteOptions ForJellyfin(RouteOptions? stored)
    {
        var profile = stored?.Profile ?? JellyfinProfile.Recommended;
        var options = For(profile);
        if (profile == JellyfinProfile.Custom && stored is not null)
        {
            options.MaxBodyMb = stored.MaxBodyMb;
            options.TimeoutSeconds = stored.TimeoutSeconds;
            options.MediaTimeoutMinutes = stored.MediaTimeoutMinutes ?? MediaTimeoutMinutes;
            options.BackendConnections = stored.BackendConnections ?? BackendConnections;
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
