using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Turns the plugin configuration into one sidecar config per tunnel: the host, and each connection.
/// </summary>
public static class SidecarConfigBuilder
{
    /// <summary>The key of the host tunnel.</summary>
    public const string HostKey = "host";

    /// <summary>The port Jellyfin answers on inside the tunnel.</summary>
    public const int TunnelJellyfinPort = 8096;

    /// <summary>
    /// Builds the config for every tunnel that should run. A tunnel missing a key or address is left
    /// out, and so is every peer or connection in <paramref name="blocked"/>.
    /// </summary>
    /// <param name="config">The WireGuard settings.</param>
    /// <param name="unprotect">Decrypts a stored key.</param>
    /// <param name="jellyfinPort">The port Jellyfin listens on locally for HTTP.</param>
    /// <param name="blocked">The peers and connections left out, such as for being over their data limit.</param>
    /// <returns>Each tunnel's config, by key.</returns>
    public static Dictionary<string, SidecarConfig> Build(
        WireGuardSettings config,
        Func<string, string> unprotect,
        int jellyfinPort,
        IReadOnlySet<Guid>? blocked = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(unprotect);
        blocked ??= new HashSet<Guid>();

        var result = new Dictionary<string, SidecarConfig>(StringComparer.Ordinal);
        var hostKey = unprotect(config.HostPrivateKey);
        if (config.Enabled && config.HostEnabled && TunnelKeys.IsValid(hostKey) && TryHostAddress(config.TunnelSubnet, out var address))
        {
            result[HostKey] = new SidecarConfig
            {
                PrivateKey = hostKey,
                ListenPort = config.ListenPort,
                Mtu = config.Mtu,
                Addresses = { address.ToString() },
                Peers = config.Peers
                    .Where(p => p.Enabled && !blocked.Contains(p.Id) && TunnelKeys.IsValid(p.PublicKey) && IPAddress.TryParse(p.Address, out _))
                    .Select(p => HostPeer(config, p, unprotect))
                    .ToList(),
                Inbound =
                {
                    new SidecarInbound
                    {
                        Port = TunnelJellyfinPort,
                        Target = "127.0.0.1:" + jellyfinPort.ToString(CultureInfo.InvariantCulture),
                        Http = true,
                        ForwardFor = config.ForwardClientAddress
                    }
                },
                Dns = config.DnsEnabled ? Dns(config) : null
            };
        }

        if (!config.Enabled || !config.ConnectEnabled)
        {
            return result;
        }

        foreach (var c in config.Connections)
        {
            var key = unprotect(c.PrivateKey);
            if (!c.Enabled || blocked.Contains(c.Id) || !TunnelKeys.IsValid(key) || !TunnelKeys.IsValid(c.PeerPublicKey)
                || string.IsNullOrWhiteSpace(c.Endpoint) || !IPAddress.TryParse(c.Address, out _))
            {
                continue;
            }

            var psk = unprotect(c.PresharedKey);
            result[c.Id.ToString("N")] = new SidecarConfig
            {
                PrivateKey = key,
                Mtu = config.Mtu,
                Addresses = { c.Address },
                Peers =
                {
                    new SidecarPeer
                    {
                        PublicKey = c.PeerPublicKey,
                        PresharedKey = TunnelKeys.IsValid(psk) ? psk : null,
                        Endpoint = c.Endpoint.Trim(),
                        AllowedIPs = { "0.0.0.0/0", "::/0" },
                        Keepalive = Math.Max(0, c.Keepalive),
                        SendMbps = c.UploadMbps ?? config.ConnectionUploadMbps,
                        RecvMbps = c.DownloadMbps ?? config.ConnectionDownloadMbps
                    }
                },
                Outbound = c.Forwards
                    .Where(f => f.LocalPort is > 0 and < 65536 && f.RemotePort is > 0 and < 65536 && IPAddress.TryParse(f.RemoteHost, out _))
                    .Select(f => new SidecarOutbound
                    {
                        Listen = "127.0.0.1:" + f.LocalPort.ToString(CultureInfo.InvariantCulture),
                        Target = f.RemoteHost + ":" + f.RemotePort.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList()
            };
        }

        return result;
    }

    /// <summary>
    /// The config with its peers left out, for telling a change that needs a restart from one that
    /// only changes peers, which the sidecar takes live.
    /// </summary>
    /// <param name="config">The tunnel's config.</param>
    /// <returns>A copy without peers.</returns>
    public static SidecarConfig WithoutPeers(SidecarConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new SidecarConfig
        {
            PrivateKey = config.PrivateKey,
            ListenPort = config.ListenPort,
            Addresses = config.Addresses,
            Inbound = config.Inbound,
            Outbound = config.Outbound,
            Dns = config.Dns,
            Mtu = config.Mtu
        };
    }

    /// <summary>Finds the host's address: the first host in the tunnel subnet.</summary>
    /// <param name="subnet">The subnet, such as 10.66.0.0/24.</param>
    /// <param name="address">The host's address, such as 10.66.0.1.</param>
    /// <returns>True when the subnet is an IPv4 network from /8 to /30, with room for peers.</returns>
    public static bool TryHostAddress(string? subnet, out IPAddress address)
    {
        address = IPAddress.None;
        if (!IPNetwork.TryParse((subnet ?? string.Empty).Trim(), out var network)
            || network.BaseAddress.AddressFamily != AddressFamily.InterNetwork
            || network.PrefixLength is > 30 or < 8)
        {
            return false;
        }

        var bytes = network.BaseAddress.GetAddressBytes();
        bytes[3]++;
        address = new IPAddress(bytes);
        return true;
    }

    /// <summary>
    /// Finds the next address in the subnet that neither the host nor a peer uses.
    /// </summary>
    /// <param name="subnet">The tunnel subnet.</param>
    /// <param name="taken">The addresses peers already use.</param>
    /// <returns>The address, or null when the subnet is full or not valid.</returns>
    public static string? NextFreeAddress(string? subnet, IEnumerable<string> taken)
    {
        if (!TryHostAddress(subnet, out var host) || !IPNetwork.TryParse(subnet!.Trim(), out var network))
        {
            return null;
        }

        var used = new HashSet<string>(taken.Select(a => (a ?? string.Empty).Trim()), StringComparer.Ordinal) { host.ToString() };
        var start = ToNumber(host) + 1;
        var size = 1u << (32 - network.PrefixLength);
        var broadcast = ToNumber(network.BaseAddress) + size - 1;
        for (var n = start; n < broadcast; n++)
        {
            var candidate = FromNumber(n).ToString();
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Checks whether an address is a usable peer address in the subnet.</summary>
    /// <param name="subnet">The tunnel subnet.</param>
    /// <param name="address">The address.</param>
    /// <returns>True when the address is inside the subnet and is not the host, the network, or the broadcast address.</returns>
    public static bool IsPeerAddress(string? subnet, string? address)
    {
        if (!TryHostAddress(subnet, out var host) || !IPNetwork.TryParse(subnet!.Trim(), out var network)
            || !IPAddress.TryParse((address ?? string.Empty).Trim(), out var ip) || !network.Contains(ip))
        {
            return false;
        }

        var n = ToNumber(ip);
        var first = ToNumber(network.BaseAddress);
        var last = first + (1u << (32 - network.PrefixLength)) - 1;
        return n != first && n != last && !ip.Equals(host);
    }

    private static SidecarPeer HostPeer(WireGuardSettings config, TunnelPeer p, Func<string, string> unprotect)
    {
        var psk = config.UsePresharedKeys ? unprotect(p.PresharedKey) : string.Empty;
        return new SidecarPeer
        {
            PublicKey = p.PublicKey,
            PresharedKey = TunnelKeys.IsValid(psk) ? psk : null,
            AllowedIPs = new[] { p.Address + "/32" }.Concat(p.RoutedSubnets.Where(IsSubnet)).ToList(),
            SendMbps = p.DownloadMbps ?? config.PeerDownloadMbps,
            RecvMbps = p.UploadMbps ?? config.PeerUploadMbps
        };
    }

    private static SidecarDns Dns(WireGuardSettings config)
    {
        var dns = new SidecarDns { Names = { config.DnsName.Trim().TrimEnd('.').ToLowerInvariant() } };
        foreach (var part in config.DnsUpstreams.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPEndPoint.TryParse(part, out var endpoint))
            {
                dns.Upstreams.Add(new IPEndPoint(endpoint.Address, endpoint.Port == 0 ? 53 : endpoint.Port).ToString());
            }
        }

        return dns;
    }

    private static uint ToNumber(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromNumber(uint n)
        => new(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });

    private static bool IsSubnet(string value) => IPNetwork.TryParse(value, out _);
}
