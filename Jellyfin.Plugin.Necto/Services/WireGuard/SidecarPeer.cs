using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>One peer in a <see cref="SidecarConfig"/>.</summary>
public sealed class SidecarPeer
{
    /// <summary>Gets or sets the peer's public key.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the plaintext preshared key, or null.</summary>
    public string? PresharedKey { get; set; }

    /// <summary>Gets or sets the peer's host:port, for outbound connections.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Gets or sets the addresses routed to the peer.</summary>
    public List<string> AllowedIPs { get; set; } = new();

    /// <summary>Gets or sets the keepalive interval in seconds, or 0 for none.</summary>
    public int Keepalive { get; set; }

    /// <summary>Gets or sets the limit on what this side sends to the peer, in Mbit/s. Zero is none.</summary>
    public double SendMbps { get; set; }

    /// <summary>Gets or sets the limit on what this side takes from the peer, in Mbit/s. Zero is none.</summary>
    public double RecvMbps { get; set; }
}
