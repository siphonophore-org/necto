using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Proxy;

/// <summary>
/// The starting points for a new route: Jellyfin open to the internet, Jellyfin for the local network only, and a
/// blank route for any other service.
/// </summary>
public static class RouteTemplates
{
    /// <summary>The key of the template that opens this Jellyfin to the internet.</summary>
    public const string Jellyfin = "jellyfin";

    /// <summary>The key of the template that serves this Jellyfin to the local network only.</summary>
    public const string JellyfinLan = "jellyfin-lan";

    /// <summary>The key of the empty template.</summary>
    public const string Blank = "blank";

    /// <summary>
    /// Builds one template's route under a domain.
    /// </summary>
    /// <param name="key">The template's key.</param>
    /// <param name="domain">The domain the route's name goes under, such as <c>example.com</c>. Empty leaves the name blank.</param>
    /// <returns>A new route with a fresh id, or <c>null</c> for an unknown key.</returns>
    public static ProxyRoute? Build(string key, string? domain)
    {
        var zone = (domain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        return key switch
        {
            // The public name the internet uses, with a certificate Let's Encrypt checks over port 80 or DNS.
            Jellyfin => new ProxyRoute
            {
                Host = zone,
                Certificate = new ProxyCertificateSettings { Mode = CertificateMode.Automatic, Challenge = CertificateChallenge.Auto }
            },

            // A name inside the house. Let's Encrypt cannot reach it, so it is checked over DNS.
            JellyfinLan => new ProxyRoute
            {
                Host = zone.Length > 0 ? "home." + zone : string.Empty,
                LocalOnly = true,
                Certificate = new ProxyCertificateSettings { Mode = CertificateMode.Automatic, Challenge = CertificateChallenge.Dns }
            },
            Blank => new ProxyRoute
            {
                Upstream = "http://",
                Certificate = new ProxyCertificateSettings { Mode = CertificateMode.Automatic, Challenge = CertificateChallenge.Auto }
            },
            _ => null
        };
    }

    /// <summary>
    /// Suggests the domain new routes go under: the zone of the first enabled DDNS record, or its name.
    /// </summary>
    /// <param name="records">The DDNS records.</param>
    /// <returns>The domain, or empty when no record names one.</returns>
    public static string SuggestDomain(IEnumerable<DNSRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records.OrderByDescending(r => r.Enabled)
            .Select(r => r.Zone.Trim().Length > 0 && r.Zone.Contains('.', StringComparison.Ordinal) ? r.Zone.Trim() : RecordTarget.ResolvableName(r))
            .Select(d => (d ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant())
            .FirstOrDefault(d => d.Length > 0) ?? string.Empty;
    }
}
