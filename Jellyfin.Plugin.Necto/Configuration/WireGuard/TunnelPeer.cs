using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.WireGuard;

/// <summary>
/// A device or router allowed to connect to the host, owned by one Jellyfin user.
/// </summary>
public class TunnelPeer
{
    /// <summary>Gets or sets the peer's identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the Jellyfin user who owns the peer.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the peer's name, such as Living room router.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer's public key.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer's private key, encrypted at rest. Empty when the peer brought its own key pair.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer's preshared key, encrypted at rest.</summary>
    public string PresharedKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer's tunnel address, such as 10.66.0.2. Empty takes the next free one on save.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the networks behind the peer, such as a router's 192.168.50.0/24.</summary>
    public List<string> RoutedSubnets { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the peer may connect.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets when the peer was created, in UTC.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the speed limit to the peer in Mbit/s, or null for the host's default.</summary>
    public int? DownloadMbps { get; set; }

    /// <summary>Gets or sets the speed limit from the peer in Mbit/s, or null for the host's default.</summary>
    public int? UploadMbps { get; set; }

    /// <summary>Gets or sets the data limit in bytes, or null for the host's default. Zero is none.</summary>
    public long? DataLimit { get; set; }

    /// <summary>Gets or sets the period the data limit counts over. It goes with <see cref="DataLimit"/>.</summary>
    public LimitPeriod DataPeriod { get; set; } = LimitPeriod.Month;
}
