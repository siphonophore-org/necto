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
    /// Gets every WireGuard event key, with the key of its name in the plugin's strings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>
    {
        [Connected] = TranslationKeys.WireGuardActivityNamePeerConnected,
        [Disconnected] = TranslationKeys.WireGuardActivityNamePeerDisconnected,
        [ConnectionEstablished] = TranslationKeys.WireGuardActivityNameConnectionEstablished,
        [ConnectionLost] = TranslationKeys.WireGuardActivityNameConnectionLost,
        [DataLimitReached] = TranslationKeys.WireGuardActivityNameDataLimitReached,
        [TunnelFailed] = TranslationKeys.WireGuardActivityNameTunnelFailed
    };
}
