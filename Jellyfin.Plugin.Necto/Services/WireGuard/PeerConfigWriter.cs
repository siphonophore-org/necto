using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Writes the WireGuard configuration a peer imports. It routes only the host's address through the
/// tunnel, so the peer's other traffic never touches this server.
/// </summary>
public static class PeerConfigWriter
{
    /// <summary>Writes a peer's configuration file.</summary>
    /// <param name="config">The WireGuard settings.</param>
    /// <param name="peer">The peer.</param>
    /// <param name="unprotect">Decrypts a stored key.</param>
    /// <returns>The file's text, or null when the host has no address or public key yet.</returns>
    public static string? Write(WireGuardSettings config, TunnelPeer peer, Func<string, string> unprotect)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(unprotect);

        if (!SidecarConfigBuilder.TryHostAddress(config.TunnelSubnet, out var host) || !TunnelKeys.IsValid(config.HostPublicKey))
        {
            return null;
        }

        var b = new StringBuilder();
        b.Append("[Interface]\n");
        var privateKey = unprotect(peer.PrivateKey);
        if (TunnelKeys.IsValid(privateKey))
        {
            b.Append("PrivateKey = ").Append(privateKey).Append('\n');
        }
        else
        {
            b.Append("# PrivateKey: add this device's own private key here\n");
        }

        b.Append("Address = ").Append(peer.Address).Append("/32\n");
        if (config.DnsEnabled)
        {
            b.Append("DNS = ").Append(host).Append('\n');
        }

        if (config.Mtu is > 0 and not 1420)
        {
            b.Append("MTU = ").Append(config.Mtu.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        b.Append("\n[Peer]\n");
        b.Append("PublicKey = ").Append(config.HostPublicKey).Append('\n');
        var psk = config.UsePresharedKeys ? unprotect(peer.PresharedKey) : string.Empty;
        if (TunnelKeys.IsValid(psk))
        {
            b.Append("PresharedKey = ").Append(psk).Append('\n');
        }

        b.Append("Endpoint = ").Append(Endpoint(config)).Append('\n');
        b.Append("AllowedIPs = ").Append(host).Append("/32\n");
        if (config.Keepalive > 0)
        {
            b.Append("PersistentKeepalive = ").Append(config.Keepalive.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return b.ToString();
    }

    /// <summary>The address peers connect to, with the listen port added when the public address has none.</summary>
    /// <param name="config">The WireGuard settings.</param>
    /// <returns>The endpoint, such as vpn.example.com:51820.</returns>
    public static string Endpoint(WireGuardSettings config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var endpoint = config.PublicEndpoint.Trim();
        var port = config.ListenPort.ToString(CultureInfo.InvariantCulture);
        if (endpoint.Length == 0)
        {
            return "YOUR-PUBLIC-ADDRESS:" + port;
        }

        // A bare IPv6 address, or a host without a port, takes the listen port.
        if (endpoint.Contains("]:", StringComparison.Ordinal)
            || (endpoint.Count(c => c == ':') == 1 && !endpoint.StartsWith('[')))
        {
            return endpoint;
        }

        if (endpoint.Contains(':', StringComparison.Ordinal) && !endpoint.StartsWith('['))
        {
            endpoint = "[" + endpoint + "]";
        }

        return endpoint + ":" + port;
    }
}
