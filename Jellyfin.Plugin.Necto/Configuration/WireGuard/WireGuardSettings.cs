using System.Collections.Generic;
using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.WireGuard;

/// <summary>
/// The WireGuard tool's settings, peers, and connections. The defaults host a split tunnel that reaches only Jellyfin,
/// with no DNS and no limits. Who may have peers, how many, and how much data they may move together is set on groups.
/// </summary>
public class WireGuardSettings
{
    /// <summary>Gets or sets a value indicating whether WireGuard runs at all. It needs the downloaded sidecar.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets a value indicating whether this server hosts peers.</summary>
    public bool HostEnabled { get; set; }

    /// <summary>Gets or sets the UDP port the host listens on.</summary>
    public int ListenPort { get; set; } = 51820;

    /// <summary>Gets or sets the public host name or address peers connect to, with an optional port.</summary>
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the tunnel subnet. The host takes its first address.</summary>
    public string TunnelSubnet { get; set; } = "10.66.0.0/24";

    /// <summary>Gets or sets the host's private key, encrypted at rest.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string HostPrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the host's public key.</summary>
    public string HostPublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether each peer gets a preshared key, a second secret on top of its key pair.</summary>
    public bool UsePresharedKeys { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether Jellyfin sees each peer's tunnel address in X-Forwarded-For.</summary>
    public bool ForwardClientAddress { get; set; } = true;

    /// <summary>Gets or sets the keepalive written into peer configurations, in seconds. Zero leaves it out.</summary>
    public int Keepalive { get; set; } = 25;

    /// <summary>Gets or sets the tunnel MTU.</summary>
    public int Mtu { get; set; } = 1420;

    /// <summary>Gets or sets a value indicating whether the host answers DNS for its peers.</summary>
    public bool DnsEnabled { get; set; }

    /// <summary>Gets or sets the name that resolves to the host inside the tunnel.</summary>
    public string DnsName { get; set; } = "jellyfin.internal";

    /// <summary>Gets or sets the resolvers that take every other query, separated by commas.</summary>
    public string DnsUpstreams { get; set; } = "1.1.1.1, 1.0.0.1";

    /// <summary>Gets or sets the default speed limit from the host to each peer, in Mbit/s. Zero is none.</summary>
    public int PeerDownloadMbps { get; set; }

    /// <summary>Gets or sets the default speed limit from each peer to the host, in Mbit/s. Zero is none.</summary>
    public int PeerUploadMbps { get; set; }

    /// <summary>Gets or sets the default data limit of each peer, in bytes. Zero is none.</summary>
    public long PeerDataLimit { get; set; }

    /// <summary>Gets or sets the period the default peer data limit counts over.</summary>
    public LimitPeriod PeerDataPeriod { get; set; } = LimitPeriod.Month;

    /// <summary>Gets or sets the peers allowed to connect to the host.</summary>
    public List<TunnelPeer> Peers { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether this server connects to other hosts.</summary>
    public bool ConnectEnabled { get; set; }

    /// <summary>Gets or sets the default speed limit from other hosts to this server, in Mbit/s. Zero is none.</summary>
    public int ConnectionDownloadMbps { get; set; }

    /// <summary>Gets or sets the default speed limit from this server to other hosts, in Mbit/s. Zero is none.</summary>
    public int ConnectionUploadMbps { get; set; }

    /// <summary>Gets or sets the default data limit of each connection, in bytes. Zero is none.</summary>
    public long ConnectionDataLimit { get; set; }

    /// <summary>Gets or sets the period the default connection data limit counts over.</summary>
    public LimitPeriod ConnectionDataPeriod { get; set; } = LimitPeriod.Month;

    /// <summary>Gets or sets the connections to other hosts.</summary>
    public List<TunnelConnection> Connections { get; set; } = new();
}
