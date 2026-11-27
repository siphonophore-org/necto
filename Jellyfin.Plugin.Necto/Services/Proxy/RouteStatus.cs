using Jellyfin.Plugin.Necto.Services.Proxy.Acme;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// What the Proxy page shows about one route: its certificate and, for Let's Encrypt, the last attempts.
/// </summary>
public sealed class RouteStatus
{
    /// <summary>Gets or sets the route's id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the route's host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the running proxy answers for the route.</summary>
    public bool Active { get; set; }

    /// <summary>Gets or sets the route's certificate.</summary>
    public CertificateStatus Certificate { get; set; } = new();

    /// <summary>Gets or sets the state of the route's Let's Encrypt certificate, when it uses one.</summary>
    public AcmeStatus Acme { get; set; } = new();

    /// <summary>Gets or sets the domain of the wildcard certificate the route uses in place of its own, or empty.</summary>
    public string SharedFrom { get; set; } = string.Empty;
}
