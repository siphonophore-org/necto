namespace Jellyfin.Plugin.Necto.Configuration.WireGuard;

/// <summary>
/// A local port that carries connections across a tunnel to a remote host.
/// </summary>
public class PortForward
{
    /// <summary>Gets or sets the local port, bound on 127.0.0.1.</summary>
    public int LocalPort { get; set; }

    /// <summary>Gets or sets the remote host across the tunnel.</summary>
    public string RemoteHost { get; set; } = string.Empty;

    /// <summary>Gets or sets the remote port.</summary>
    public int RemotePort { get; set; }
}
