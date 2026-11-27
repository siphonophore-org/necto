using System;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// One enabled route as the running proxy sees it.
/// </summary>
/// <param name="Id">The route's id, which is also its YARP route and cluster id.</param>
/// <param name="Host">The normalized host name or wildcard.</param>
/// <param name="Backend">Where requests go.</param>
/// <param name="IsJellyfin">Whether it forwards to this Jellyfin, which turns on the Jellyfin side checks.</param>
/// <param name="LocalOnly">Whether only the local network and trusted networks may reach it.</param>
/// <param name="Options">How the route forwards, resolved from its profile for Jellyfin, or <c>null</c> for the settings in Settings.</param>
/// <param name="LocalHost">The normalized local address of a route to Jellyfin, which only local clients reach, or empty.</param>
public sealed record RuntimeRoute(string Id, string Host, Uri Backend, bool IsJellyfin, bool LocalOnly, RouteOptions? Options = null, string LocalHost = "")
{
    /// <summary>
    /// Tells whether the route answers a name for its own host or its local address.
    /// </summary>
    /// <param name="host">The request host, without port.</param>
    /// <returns><c>true</c> when either matches.</returns>
    public bool Answers(string? host)
        => HostnameMatcher.Matches(host, Host) || (LocalHost.Length > 0 && HostnameMatcher.Matches(host, LocalHost));

    /// <summary>
    /// Tells whether only local clients may reach the route by a name: always for a local only route, and for its
    /// local address otherwise.
    /// </summary>
    /// <param name="host">The request host, without port.</param>
    /// <returns><c>true</c> when the internet gets nothing for the name.</returns>
    public bool LocalOnlyFor(string? host)
        => LocalOnly || (LocalHost.Length > 0 && HostnameMatcher.Matches(host, LocalHost) && !HostnameMatcher.Matches(host, Host));
}
