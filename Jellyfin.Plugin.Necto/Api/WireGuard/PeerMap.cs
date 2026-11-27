using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.WireGuard;

namespace Jellyfin.Plugin.Necto.Api.WireGuard;

/// <summary>
/// Joins the stored peers and connections with what the running tunnels report, into the list the Dashboard map and
/// the WireGuard tool draw.
/// </summary>
public static class PeerMap
{
    /// <summary>
    /// Builds every tunnel end: the host while it runs, each of its peers, and each connection.
    /// </summary>
    /// <param name="settings">The WireGuard settings.</param>
    /// <param name="tunnels">What the running tunnels report.</param>
    /// <param name="blocked">The peers and connections over their data limit.</param>
    /// <param name="refused">The peers their owners' groups leave out.</param>
    /// <param name="userName">Names a user, or gives null for one the server does not have.</param>
    /// <returns>The tunnel ends, the host first.</returns>
    public static List<PeerConnection> Build(
        WireGuardSettings settings,
        IReadOnlyList<TunnelStatus> tunnels,
        IReadOnlyCollection<Guid> blocked,
        IReadOnlyDictionary<Guid, PeerRefusal> refused,
        Func<Guid, string?> userName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tunnels);
        ArgumentNullException.ThrowIfNull(blocked);
        ArgumentNullException.ThrowIfNull(refused);
        ArgumentNullException.ThrowIfNull(userName);

        var result = new List<PeerConnection>();
        var host = tunnels.FirstOrDefault(t => t.Key == SidecarConfigBuilder.HostKey);
        if (settings.Enabled && settings.HostEnabled)
        {
            result.Add(new PeerConnection
            {
                Kind = "Host",
                Name = "Host",
                RemoteAddress = string.IsNullOrWhiteSpace(settings.PublicEndpoint) ? null : TunnelActivity.HostOf(PeerConfigWriter.Endpoint(settings)),
                Connected = host?.State == "Running",
                State = host?.State ?? "Stopped",
                Error = host?.State == "Failed" ? host.Error : null
            });
        }

        var peerStates = (host?.Peers ?? new List<PeerStatus>()).ToDictionary(p => p.PublicKey, StringComparer.Ordinal);
        foreach (var peer in settings.Peers)
        {
            peerStates.TryGetValue(peer.PublicKey, out var state);
            result.Add(new PeerConnection
            {
                Id = peer.Id,
                Kind = "Peer",
                Name = peer.Name,
                UserId = peer.UserId,
                UserName = peer.UserId == Guid.Empty ? null : userName(peer.UserId),
                RemoteAddress = state?.Endpoint is { } endpoint ? TunnelActivity.HostOf(endpoint) : null,
                Connected = state?.Connected == true,
                State = StateOf(peer.Id, peer.Enabled && settings.HostEnabled, state?.Connected == true, blocked, refused),
                LastHandshakeUtc = state?.LastHandshakeUtc,
                Bytes = (state?.RxBytes ?? 0) + (state?.TxBytes ?? 0)
            });
        }

        foreach (var connection in settings.Connections)
        {
            var tunnel = tunnels.FirstOrDefault(t => t.Key == connection.Id.ToString("N"));
            var state = tunnel?.Peers.FirstOrDefault();
            result.Add(new PeerConnection
            {
                Id = connection.Id,
                Kind = "Connection",
                Name = connection.Name,
                RemoteAddress = TunnelActivity.HostOf(state?.Endpoint ?? connection.Endpoint),
                Connected = state?.Connected == true,
                State = tunnel?.State == "Failed" ? "Failed" : StateOf(connection.Id, connection.Enabled && settings.ConnectEnabled, state?.Connected == true, blocked, new Dictionary<Guid, PeerRefusal>()),
                Error = tunnel?.State == "Failed" ? tunnel.Error : null,
                LastHandshakeUtc = state?.LastHandshakeUtc,
                Bytes = (state?.RxBytes ?? 0) + (state?.TxBytes ?? 0)
            });
        }

        return result;
    }

    private static string StateOf(Guid id, bool enabled, bool connected, IReadOnlyCollection<Guid> blocked, IReadOnlyDictionary<Guid, PeerRefusal> refused)
    {
        if (refused.TryGetValue(id, out var why))
        {
            return why.ToString();
        }

        if (blocked.Contains(id))
        {
            return "OverLimit";
        }

        if (!enabled)
        {
            return "Disabled";
        }

        return connected ? "Connected" : "Offline";
    }
}
