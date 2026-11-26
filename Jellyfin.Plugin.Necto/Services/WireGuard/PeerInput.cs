using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// The fields of a peer the editor sends. Keys never travel in it, apart from a public key a new peer brings.
/// </summary>
public sealed class PeerInput
{
    /// <summary>Gets or sets the peer's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin user who owns the peer.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the tunnel address, or blank for the next free one.</summary>
    public string? Address { get; set; }

    /// <summary>Gets or sets the public key a new peer brings, or blank to make a new key pair.</summary>
    public string? PublicKey { get; set; }

    /// <summary>Gets or sets the networks behind the peer.</summary>
    public List<string>? RoutedSubnets { get; set; }

    /// <summary>Gets or sets a value indicating whether the peer may connect.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the speed limit to the peer in Mbit/s, or null for the default.</summary>
    public int? DownloadMbps { get; set; }

    /// <summary>Gets or sets the speed limit from the peer in Mbit/s, or null for the default.</summary>
    public int? UploadMbps { get; set; }

    /// <summary>Gets or sets the data limit in bytes, or null for the default.</summary>
    public long? DataLimit { get; set; }

    /// <summary>Gets or sets the period the data limit counts over.</summary>
    public LimitPeriod DataPeriod { get; set; } = LimitPeriod.Month;
}
