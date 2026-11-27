using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;

namespace Jellyfin.Plugin.Necto.Tests.Fakes;

/// <summary>
/// Builds the Let's Encrypt ask of the route r0 under a test folder, as the Jellyfin template makes it.
/// </summary>
public static class TestAcme
{
    /// <summary>The id of the route the tests ask for.</summary>
    public const string RouteId = "r0";

    /// <summary>
    /// Builds the ask for names under a test folder.
    /// </summary>
    /// <param name="dir">The proxy's data folder.</param>
    /// <param name="names">The names on the certificate.</param>
    /// <param name="challenge">How Let's Encrypt checks them.</param>
    /// <returns>The request.</returns>
    public static AcmeRequest Request(string dir, IEnumerable<string> names, CertificateChallenge challenge = CertificateChallenge.Auto)
        => new(new ProxyPaths(dir).ForRoute(RouteId), names.ToList(), challenge, string.Empty);

    /// <summary>
    /// Gives the route r0's certificate files under a test folder.
    /// </summary>
    /// <param name="paths">The proxy's data paths.</param>
    /// <returns>The route's files.</returns>
    public static CertificatePaths Slot(ProxyPaths paths) => paths.ForRoute(RouteId);
}
