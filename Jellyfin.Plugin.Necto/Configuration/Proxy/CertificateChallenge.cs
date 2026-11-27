namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// How Let's Encrypt checks that a route's name belongs to this server.
/// </summary>
public enum CertificateChallenge
{
    /// <summary>A DNS challenge when a DDNS record can publish the TXT record, otherwise an HTTP challenge.</summary>
    Auto,

    /// <summary>An HTTP challenge on port 80, which needs the name to reach this server from the internet.</summary>
    Http,

    /// <summary>A DNS challenge through a DDNS provider, which needs no open port and covers wildcards and local names.</summary>
    Dns
}
