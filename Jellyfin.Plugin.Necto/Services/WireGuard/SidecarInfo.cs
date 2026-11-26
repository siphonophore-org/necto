using System;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// What Settings shows about the downloaded sidecar.
/// </summary>
public sealed class SidecarInfo
{
    /// <summary>Gets or sets a value indicating whether the sidecar is downloaded.</summary>
    public bool Installed { get; set; }

    /// <summary>Gets or sets the release the kept sidecar came from, or null when not known.</summary>
    public string? Version { get; set; }

    /// <summary>Gets or sets when the kept sidecar was downloaded, in UTC, or null.</summary>
    public DateTime? DownloadedUtc { get; set; }

    /// <summary>Gets or sets this server's platform, such as linux-amd64, or null when no sidecar is built for it.</summary>
    public string? Platform { get; set; }

    /// <summary>Gets or sets the release Necto downloads.</summary>
    public string ReleaseVersion { get; set; } = string.Empty;
}
