using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// The state of one running tunnel: the host, or one connection.
/// </summary>
public sealed class TunnelStatus
{
    /// <summary>Gets or sets the tunnel's key: host, or the connection's identifier.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the tunnel's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the state: Starting, Running, or Failed.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets the last error the sidecar reported, or null.</summary>
    public string? Error { get; set; }


    /// <summary>Gets or sets each peer's traffic and handshake.</summary>
    public List<PeerStatus> Peers { get; set; } = new();
}
