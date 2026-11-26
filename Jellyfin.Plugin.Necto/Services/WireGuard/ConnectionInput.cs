using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// The fields of a connection the editor sends. A key field left blank keeps the stored key.
/// </summary>
public sealed class ConnectionInput
{
    /// <summary>Gets or sets the connection's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the remote host, as host:port.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the remote host's public key.</summary>
    public string PeerPublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets this server's tunnel address on the connection.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the local ports that reach hosts across the tunnel.</summary>
    public List<PortForward>? Forwards { get; set; }

    /// <summary>Gets or sets a value indicating whether the connection is up.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the keepalive in seconds.</summary>
    public int Keepalive { get; set; } = 25;

    /// <summary>Gets or sets the speed limit from the remote host in Mbit/s, or null for the default.</summary>
    public int? DownloadMbps { get; set; }

    /// <summary>Gets or sets the speed limit to the remote host in Mbit/s, or null for the default.</summary>
    public int? UploadMbps { get; set; }

    /// <summary>Gets or sets the data limit in bytes, or null for the default.</summary>
    public long? DataLimit { get; set; }

    /// <summary>Gets or sets the period the data limit counts over.</summary>
    public LimitPeriod DataPeriod { get; set; } = LimitPeriod.Month;

    /// <summary>Gets or sets a pasted private key, or blank to keep the stored one.</summary>
    public string? NewPrivateKey { get; set; }

    /// <summary>Gets or sets a pasted preshared key, or blank to keep the stored one.</summary>
    public string? NewPresharedKey { get; set; }
}
