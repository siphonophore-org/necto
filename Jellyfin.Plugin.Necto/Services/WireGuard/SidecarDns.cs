using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>The DNS a <see cref="SidecarConfig"/> answers.</summary>
public sealed class SidecarDns
{
    /// <summary>Gets or sets the names that resolve to the tunnel address.</summary>
    public List<string> Names { get; set; } = new();

    /// <summary>Gets or sets the resolvers that take every other query, as ip:port.</summary>
    public List<string> Upstreams { get; set; } = new();
}
