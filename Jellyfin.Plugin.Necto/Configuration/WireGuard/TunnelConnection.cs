using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.WireGuard;

/// <summary>
/// A connection from this server to another WireGuard host, as one of its peers.
/// </summary>
public class TunnelConnection
{
    /// <summary>Gets or sets the connection's identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the connection's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the connection is up.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the remote host, as host:port.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the remote host's public key.</summary>
    public string PeerPublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional preshared key, encrypted at rest.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string PresharedKey { get; set; } = string.Empty;

    /// <summary>Gets or sets this server's private key on the connection, encrypted at rest.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets this server's public key on the connection, to give the remote host.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets this server's tunnel address on the connection, such as 10.0.0.5.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the keepalive in seconds. Zero is none.</summary>
    public int Keepalive { get; set; } = 25;

    /// <summary>Gets or sets the local ports that reach hosts across the tunnel.</summary>
    public List<PortForward> Forwards { get; set; } = new();

    /// <summary>Gets or sets the speed limit from the remote host in Mbit/s, or null for the default.</summary>
    public int? DownloadMbps { get; set; }

    /// <summary>Gets or sets the speed limit to the remote host in Mbit/s, or null for the default.</summary>
    public int? UploadMbps { get; set; }

    /// <summary>Gets or sets the data limit in bytes, or null for the default. Zero is none.</summary>
    public long? DataLimit { get; set; }

    /// <summary>Gets or sets the period the data limit counts over. It goes with <see cref="DataLimit"/>.</summary>
    public LimitPeriod DataPeriod { get; set; } = LimitPeriod.Month;
}
