using System;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// One enabled route as the running proxy sees it.
/// </summary>
/// <param name="Id">The route's id, which is also its YARP route and cluster id.</param>
/// <param name="Host">The normalized host name or wildcard.</param>
/// <param name="Backend">Where requests go.</param>
/// <param name="IsJellyfin">Whether it forwards to this Jellyfin, which turns on the Jellyfin side checks.</param>
/// <param name="LocalOnly">Whether only the local network and trusted networks may reach it.</param>
public sealed record RuntimeRoute(string Id, string Host, Uri Backend, bool IsJellyfin, bool LocalOnly);
