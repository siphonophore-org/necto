using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// The config one wgsidecar process reads on stdin. Serialized in camel case to match the sidecar.
/// </summary>
public sealed class SidecarConfig
{
    /// <summary>Gets or sets the plaintext private key. Never written to disk.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the UDP port to listen on, or 0 for any.</summary>
    public int ListenPort { get; set; }

    /// <summary>Gets or sets the tunnel addresses.</summary>
    public List<string> Addresses { get; set; } = new();

    /// <summary>Gets or sets the peers.</summary>
    public List<SidecarPeer> Peers { get; set; } = new();

    /// <summary>Gets or sets the tunnel ports forwarded to local targets.</summary>
    public List<SidecarInbound> Inbound { get; set; } = new();

    /// <summary>Gets or sets the local ports forwarded across the tunnel.</summary>
    public List<SidecarOutbound> Outbound { get; set; } = new();

    /// <summary>Gets or sets the DNS the tunnel answers, or null for none.</summary>
    public SidecarDns? Dns { get; set; }

    /// <summary>Gets or sets the tunnel MTU, or 0 for the sidecar's default.</summary>
    public int Mtu { get; set; }
}
