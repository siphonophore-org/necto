namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>A local port forwarded across the tunnel.</summary>
public sealed class SidecarOutbound
{
    /// <summary>Gets or sets the local host:port.</summary>
    public string Listen { get; set; } = string.Empty;

    /// <summary>Gets or sets the host:port across the tunnel.</summary>
    public string Target { get; set; } = string.Empty;
}
