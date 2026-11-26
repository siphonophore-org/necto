using System;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>One peer's traffic and handshake.</summary>
public sealed class PeerStatus
{
    /// <summary>Gets or sets the peer's public key.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the address the peer last connected from.</summary>
    public string? Endpoint { get; set; }


    /// <summary>Gets or sets the last handshake, in UTC, or null when there was none.</summary>
    public DateTime? LastHandshakeUtc { get; set; }

    /// <summary>Gets or sets the bytes received from the peer.</summary>
    public long RxBytes { get; set; }

    /// <summary>Gets or sets the bytes sent to the peer.</summary>
    public long TxBytes { get; set; }

    /// <summary>Gets or sets a value indicating whether the peer is connected.</summary>
    public bool Connected { get; set; }
}
