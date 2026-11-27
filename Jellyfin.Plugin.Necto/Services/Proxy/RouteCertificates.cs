using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// Works out the names a route's certificate covers, and which route's wildcard certificate another route shares. A
/// wildcard certificate covers its domain and every name one label under it, so <c>example.com</c> with a wildcard
/// covers <c>example.com</c>, <c>media.example.com</c>, and <c>home.example.com</c>.
/// </summary>
public static class RouteCertificates
{
    /// <summary>
    /// Gives the domain a route's wildcard certificate covers: its host without a leading <c>*.</c>.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The normalized domain, or empty when the route has no host.</returns>
    public static string WildcardDomain(ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var host = HostnameMatcher.Normalize(route.Host);
        return host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
    }

    /// <summary>
    /// Tells whether a route asks Let's Encrypt for a wildcard certificate.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns><c>true</c> for an enabled route with Let's Encrypt and Wildcard on.</returns>
    public static bool IsWildcard(ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Enabled && route.Certificate is { Mode: CertificateMode.Automatic, Wildcard: true } && WildcardDomain(route).Length > 0;
    }

    /// <summary>
    /// Lists the names Let's Encrypt is asked for, leaving out names it can never check, such as <c>nas.lan</c>. A
    /// wildcard asks for its domain and the names under it, and a route to Jellyfin adds its local address when the
    /// wildcard does not cover it.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>The names, each once.</returns>
    public static List<string> Names(ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var names = new List<string>();
        if (route.Certificate?.Wildcard == true && route.Certificate.Mode == CertificateMode.Automatic)
        {
            var domain = WildcardDomain(route);
            if (domain.Length > 0 && HostnameMatcher.IsPublicName(domain))
            {
                names.Add(domain);
                names.Add("*." + domain);
            }
        }
        else
        {
            var host = HostnameMatcher.Normalize(route.Host);
            if (host.Length > 0 && HostnameMatcher.IsPublicName(host))
            {
                names.Add(host);
            }
        }

        var local = route.IsJellyfin ? HostnameMatcher.Normalize(route.LocalHost) : string.Empty;
        if (local.Length > 0 && HostnameMatcher.IsPublicName(local) && !names.Any(n => HostnameMatcher.Matches(local, n)))
        {
            names.Add(local);
        }

        return names;
    }

    /// <summary>
    /// Tells whether a wildcard certificate for a domain covers a name: the domain itself, or one label under it.
    /// </summary>
    /// <param name="name">The normalized name.</param>
    /// <param name="domain">The normalized domain.</param>
    /// <returns><c>true</c> when the certificate covers the name.</returns>
    public static bool Covers(string name, string domain)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(domain);
        return domain.Length > 0 && (string.Equals(name, domain, StringComparison.Ordinal) || HostnameMatcher.Matches(name, "*." + domain) || string.Equals(name, "*." + domain, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the other route whose wildcard certificate covers a route's names, which the route uses in place of its
    /// own.
    /// </summary>
    /// <param name="routes">Every route.</param>
    /// <param name="route">The route.</param>
    /// <returns>The covering route, or <c>null</c> when the route keeps its own certificate.</returns>
    public static ProxyRoute? CoveredBy(IEnumerable<ProxyRoute> routes, ProxyRoute route)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(route);
        var host = HostnameMatcher.Normalize(route.Host);
        if (host.Length == 0)
        {
            return null;
        }

        var local = route.IsJellyfin ? HostnameMatcher.Normalize(route.LocalHost) : string.Empty;
        return routes.FirstOrDefault(other => !ReferenceEquals(other, route)
            && !string.Equals(other.Id, route.Id, StringComparison.Ordinal)
            && IsWildcard(other)
            && Covers(host, WildcardDomain(other))
            && (local.Length == 0 || Covers(local, WildcardDomain(other))));
    }
}
