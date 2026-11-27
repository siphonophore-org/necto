using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Refuses WireGuard settings that could not run. The pages check the same things, but settings can arrive from any
/// API client.
/// </summary>
public static partial class WireGuardValidator
{
    /// <summary>
    /// Checks the WireGuard settings of a configuration save, and keeps the stored peers, connections, and host keys,
    /// which only the WireGuard routes change. A page that read the settings earlier never undoes a peer added since.
    /// A tunnel subnet that would leave a stored peer outside it is refused.
    /// </summary>
    /// <param name="incoming">The settings about to be saved, or null.</param>
    /// <param name="stored">The settings in effect.</param>
    /// <returns>The settings to save.</returns>
    /// <exception cref="ArgumentException">When a setting is not valid, or a stored peer is outside the subnet.</exception>
    public static WireGuardSettings Saving(WireGuardSettings? incoming, WireGuardSettings? stored)
    {
        var result = incoming ?? new WireGuardSettings();
        stored ??= new WireGuardSettings();
        result.Peers = stored.Peers;
        result.Connections = stored.Connections;
        result.HostPrivateKey = stored.HostPrivateKey;
        result.HostPublicKey = stored.HostPublicKey;
        Validate(result);
        ValidateAddresses(result);
        return result;
    }

    /// <summary>Checks the settings, before keys and addresses are filled in.</summary>
    /// <param name="config">The incoming configuration.</param>
    /// <exception cref="ArgumentException">When a setting is not valid.</exception>
    public static void Validate(WireGuardSettings config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.HostEnabled && !SidecarConfigBuilder.TryHostAddress(config.TunnelSubnet, out _))
        {
            throw new ArgumentException("The tunnel subnet must be an IPv4 network from /8 to /30, such as 10.66.0.0/24.");
        }

        if (config.ListenPort is < 1 or > 65535)
        {
            throw new ArgumentException("The listen port must be from 1 to 65535.");
        }

        if (config.Mtu is < 1280 or > 1500)
        {
            throw new ArgumentException("The MTU must be from 1280 to 1500.");
        }

        if (config.Keepalive is < 0 or > 3600)
        {
            throw new ArgumentException("The keepalive must be from 0 to 3600 seconds.");
        }

        if (config.DnsEnabled)
        {
            if (!DnsNamePattern().IsMatch(config.DnsName.Trim().TrimEnd('.')))
            {
                throw new ArgumentException("The DNS name must be a host name, such as jellyfin.internal.");
            }

            var upstreams = config.DnsUpstreams.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (upstreams.Length == 0 || upstreams.Any(u => !IPEndPoint.TryParse(u, out _)))
            {
                throw new ArgumentException("Each upstream resolver must be an IP address, such as 1.1.1.1.");
            }
        }

        var limits = new long[]
        {
            config.PeerDownloadMbps, config.PeerUploadMbps, config.PeerDataLimit,
            config.ConnectionDownloadMbps, config.ConnectionUploadMbps, config.ConnectionDataLimit
        }
            .Concat(config.Peers.SelectMany(p => new long?[] { p.DownloadMbps, p.UploadMbps, p.DataLimit }).OfType<long>())
            .Concat(config.Connections.SelectMany(c => new long?[] { c.DownloadMbps, c.UploadMbps, c.DataLimit }).OfType<long>());
        if (limits.Any(l => l < 0))
        {
            throw new ArgumentException("Limits cannot be negative.");
        }
    }

    /// <summary>Checks peer and connection addresses, after empty ones are filled in.</summary>
    /// <param name="config">The incoming configuration.</param>
    /// <exception cref="ArgumentException">When an address is not valid or is used twice.</exception>
    public static void ValidateAddresses(WireGuardSettings config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var seen = new HashSet<IPAddress>();
        var ids = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(config.HostPublicKey))
        {
            keys.Add(config.HostPublicKey);
        }

        // With no valid subnet, IsPeerAddress below refuses every peer, so the overlap check has nothing to add.
        var hasTunnel = IPNetwork.TryParse((config.TunnelSubnet ?? string.Empty).Trim(), out var tunnel);
        foreach (var peer in config.Peers)
        {
            if (!ids.Add(peer.Id))
            {
                throw new ArgumentException($"More than one peer has the identifier {peer.Id}.");
            }

            if (!SidecarConfigBuilder.IsPeerAddress(config.TunnelSubnet, peer.Address))
            {
                throw new ArgumentException($"{peer.Address} is not a peer address in {config.TunnelSubnet}.");
            }

            // Compared as addresses, so 10.66.0.2 and " 10.66.0.2" are one address.
            if (!seen.Add(IPAddress.Parse(peer.Address.Trim())))
            {
                throw new ArgumentException($"{peer.Address} is used by more than one peer.");
            }

            // Two peers on one key would share one session, and the second would take the first's routes.
            if (!string.IsNullOrEmpty(peer.PublicKey) && !keys.Add(peer.PublicKey))
            {
                throw new ArgumentException($"The public key of {peer.Name} is used by the host or another peer.");
            }

            foreach (var subnet in peer.RoutedSubnets)
            {
                if (!IPNetwork.TryParse(subnet, out var child))
                {
                    throw new ArgumentException($"A child network of {peer.Name} is not a subnet, such as 192.168.1.0/24.");
                }

                // A child network over the tunnel would let the peer send as other peers' addresses.
                if (hasTunnel && Overlaps(child, tunnel))
                {
                    throw new ArgumentException($"A child network of {peer.Name} overlaps the tunnel subnet.");
                }
            }
        }

        var connectionIds = new HashSet<Guid>();
        foreach (var connection in config.Connections)
        {
            if (!connectionIds.Add(connection.Id))
            {
                throw new ArgumentException($"More than one connection has the identifier {connection.Id}.");
            }

            if (!string.IsNullOrEmpty(connection.Address) && !IPAddress.TryParse(connection.Address, out _))
            {
                throw new ArgumentException($"The address of {connection.Name} is not an IP address.");
            }

            var ports = connection.Forwards.Select(f => f.LocalPort).ToList();
            if (ports.Distinct().Count() != ports.Count
                || config.Connections.Where(c => c != connection).SelectMany(c => c.Forwards).Any(f => ports.Contains(f.LocalPort)))
            {
                throw new ArgumentException($"A local port of {connection.Name} is used by more than one forward.");
            }
        }
    }

    // Whether two networks share any address. One of two IPv4 networks always holds the other's base
    // address when they overlap.
    private static bool Overlaps(IPNetwork a, IPNetwork b)
        => a.BaseAddress.AddressFamily == b.BaseAddress.AddressFamily && b.PrefixLength > 0
            && (a.Contains(b.BaseAddress) || b.Contains(a.BaseAddress));

    // One or more labels of letters, digits, and hyphens, separated by dots.
    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(\.[a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex DnsNamePattern();
}
