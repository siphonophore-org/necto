namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>A tunnel port forwarded to a local target.</summary>
public sealed class SidecarInbound
{
    /// <summary>Gets or sets the tunnel port.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the local host:port.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether requests are proxied as HTTP.</summary>
    public bool Http { get; set; }

    /// <summary>Gets or sets a value indicating whether the peer's tunnel address goes in X-Forwarded-For.</summary>
    public bool ForwardFor { get; set; }
}
