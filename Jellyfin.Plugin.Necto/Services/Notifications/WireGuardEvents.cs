using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Services.Notifications;

/// <summary>
/// The events WireGuard sends to Notifications, in one place so Notifications can list them.
/// </summary>
public static class WireGuardEvents
{
    /// <summary>A peer made its first handshake with the host.</summary>
    public const string Connected = "wireguard.connected";

    /// <summary>A peer went quiet.</summary>
    public const string Disconnected = "wireguard.disconnected";

    /// <summary>This server reached another WireGuard host.</summary>
    public const string ConnectionEstablished = "wireguard.connection-established";

    /// <summary>This server lost its connection to another WireGuard host.</summary>
    public const string ConnectionLost = "wireguard.connection-lost";

    /// <summary>A peer or connection reached its data limit and was cut off until its period starts over.</summary>
    public const string DataLimitReached = "wireguard.data-limit";

    /// <summary>A tunnel keeps failing to start.</summary>
    public const string TunnelFailed = "wireguard.failed";

    /// <summary>
    /// Gets every WireGuard event, in the order Notifications lists them.
    /// </summary>
    public static IReadOnlyList<NectoEventInfo> All { get; } = new[]
    {
        new NectoEventInfo(Connected, TranslationKeys.WireGuardActivityNamePeerConnected),
        new NectoEventInfo(Disconnected, TranslationKeys.WireGuardActivityNamePeerDisconnected),
        new NectoEventInfo(ConnectionEstablished, TranslationKeys.WireGuardActivityNameConnectionEstablished),
        new NectoEventInfo(ConnectionLost, TranslationKeys.WireGuardActivityNameConnectionLost),
        new NectoEventInfo(DataLimitReached, TranslationKeys.WireGuardActivityNameDataLimitReached),
        new NectoEventInfo(TunnelFailed, TranslationKeys.WireGuardActivityNameTunnelFailed)
    };
}
