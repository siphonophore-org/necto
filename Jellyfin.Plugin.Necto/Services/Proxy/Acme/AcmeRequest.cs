using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Proxy.Acme;

/// <summary>
/// One route's ask of Let's Encrypt: where its files go, the names it covers, and how they are checked.
/// </summary>
/// <param name="Slot">The route's certificate files.</param>
/// <param name="Hostnames">The names on the certificate.</param>
/// <param name="Challenge">How Let's Encrypt checks the names.</param>
/// <param name="DnsRecordId">The DDNS record a DNS challenge uses, or empty to pick by zone.</param>
public sealed record AcmeRequest(CertificatePaths Slot, IReadOnlyList<string> Hostnames, CertificateChallenge Challenge, string DnsRecordId);
