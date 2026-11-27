using System;

namespace Jellyfin.Plugin.Necto.Api.WireGuard;

/// <summary>
/// One end of a WireGuard tunnel as the Dashboard map places it: a peer of the host, a connection to another host, or
/// the host itself.
/// </summary>
public sealed class PeerConnection
{
    /// <summary>Gets or sets the peer's or connection's id, or empty for the host.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets what it is: Peer, Connection, or Host.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets its name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the owner of a peer, or empty.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the owner's name, or null.</summary>
    public string? UserName { get; set; }

    /// <summary>Gets or sets the public address it connects from or to, without the port, or null when not known.</summary>
    public string? RemoteAddress { get; set; }

    /// <summary>Gets or sets a value indicating whether it is connected.</summary>
    public bool Connected { get; set; }

    /// <summary>Gets or sets its state: Connected, Offline, Disabled, OverLimit, NotAllowed, OverPeerCount, Running, Starting, Failed, or Stopped.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Gets or sets why its tunnel failed, or null.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the last handshake, in UTC, or null.</summary>
    public DateTime? LastHandshakeUtc { get; set; }

    /// <summary>Gets or sets the bytes it received and sent since its tunnel started.</summary>
    public long Bytes { get; set; }
}
