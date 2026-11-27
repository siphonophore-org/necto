using Siphonophore.Stolon;

namespace Jellyfin.Plugin.Necto.Configuration.Proxy;

/// <summary>
/// Where one route's TLS certificate comes from.
/// </summary>
public class ProxyCertificateSettings
{
    /// <summary>Gets or sets the certificate source.</summary>
    public CertificateMode Mode { get; set; } = CertificateMode.Automatic;

    /// <summary>Gets or sets how Let's Encrypt checks the name, for <see cref="CertificateMode.Automatic"/>.</summary>
    public CertificateChallenge Challenge { get; set; } = CertificateChallenge.Auto;

    /// <summary>Gets or sets the id of the DDNS record whose provider publishes the TXT record for a DNS challenge. Empty picks the record whose zone covers the name.</summary>
    public string DnsRecordId { get; set; } = string.Empty;

    /// <summary>Gets or sets the PKCS#12 (.pfx) file, or the PEM certificate chain file. An uploaded certificate is kept in the route's folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the PEM private key file. Only used in <see cref="CertificateMode.PemFiles"/> mode.</summary>
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the PKCS#12 password, or the PEM key password. Stored encrypted.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(KeptSecretConverter))]
    public string Password { get; set; } = string.Empty;
}
