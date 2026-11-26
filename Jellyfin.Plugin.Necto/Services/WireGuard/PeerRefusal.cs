namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Why the tunnel leaves a peer out for its owner's groups.
/// </summary>
public enum PeerRefusal
{
    /// <summary>No group of the owner allows peers.</summary>
    NotAllowed,

    /// <summary>The owner has more enabled peers than their groups give.</summary>
    OverPeerCount
}
