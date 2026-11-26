using System.IO;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Where one route keeps its certificate files, in a folder of its own under the proxy's data folder.
/// </summary>
/// <param name="Directory">The route's folder.</param>
public sealed record CertificatePaths(string Directory)
{
    /// <summary>Gets the chain Let's Encrypt issued, as PEM, with its key after it.</summary>
    public string AcmeChainFile => Path.Join(Directory, "fullchain.pem");

    /// <summary>Gets the issued certificate's private key, as PEM.</summary>
    public string AcmeKeyFile => Path.Join(Directory, "privkey.pem");

    /// <summary>Gets the record of issuance attempts for this route.</summary>
    public string AcmeStateFile => Path.Join(Directory, "acme-state.json");

    /// <summary>Gets the self-signed certificate the route serves, issued from the proxy's own root.</summary>
    public string SelfSignedFile => Path.Join(Directory, "selfsigned.pfx");

    /// <summary>Gets an uploaded PKCS#12 certificate, or an uploaded PEM chain.</summary>
    public string UploadedFile => Path.Join(Directory, "uploaded.cert");

    /// <summary>Gets the private key uploaded with a PEM chain.</summary>
    public string UploadedKeyFile => Path.Join(Directory, "uploaded.key");
}
