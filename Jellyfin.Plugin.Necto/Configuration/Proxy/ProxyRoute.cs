using System;

namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// One host name the proxy answers for, the service it forwards to, its certificate, and who may reach it.
/// </summary>
public class ProxyRoute
{
    /// <summary>Gets or sets the route's id, which names its certificate folder.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets a value indicating whether the proxy answers for the route.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the host name, such as <c>media.example.com</c>, or a one label wildcard such as <c>*.example.com</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the address forwarded to, such as <c>http://192.168.1.20:8096</c>. Empty forwards to this Jellyfin.</summary>
    public string Upstream { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the route answers only the local network and trusted networks.</summary>
    public bool LocalOnly { get; set; }

    /// <summary>Gets or sets where the route's certificate comes from.</summary>
    public ProxyCertificateSettings Certificate { get; set; } = new();

    /// <summary>Gets a value indicating whether the route forwards to this Jellyfin, which turns on the Jellyfin side checks.</summary>
    public bool IsJellyfin => string.IsNullOrWhiteSpace(Upstream);
}
