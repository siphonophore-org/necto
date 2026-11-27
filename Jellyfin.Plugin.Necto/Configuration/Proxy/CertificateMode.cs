using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Api.Ddns;
namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// Where the proxy's TLS certificate comes from.
/// </summary>
public enum CertificateMode
{
    /// <summary>No certificate. The HTTPS listener does not start.</summary>
    None,

    /// <summary>Issued and renewed from Let's Encrypt, proved over HTTP-01 through the proxy's HTTP listener or over DNS-01 through a DDNS record.</summary>
    Automatic,

    /// <summary>A PKCS#12 (.pfx/.p12) file, reloaded when it changes.</summary>
    Pkcs12File,

    /// <summary>PEM certificate and key files (for example from certbot or acme.sh), reloaded when they change.</summary>
    PemFiles,

    /// <summary>A self-signed certificate generated and kept by the plugin. Useful on the LAN and for testing only.</summary>
    SelfSigned
}
