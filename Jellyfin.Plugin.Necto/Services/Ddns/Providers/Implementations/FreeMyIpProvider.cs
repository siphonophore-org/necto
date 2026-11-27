using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;

namespace Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;

/// <summary>
/// freemyip.com (port of ddclient's <c>nic_freemyip_update</c>). Hostname is the domain, Password is the
/// API token. One GET to the update endpoint covers both IPv4 and IPv6.
/// </summary>
public sealed class FreeMyIpProvider : DNSProviderBase
{
    private const string DefaultServer = "freemyip.com";

    /// <summary>Initializes a new instance of the <see cref="FreeMyIpProvider"/> class.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public FreeMyIpProvider(IHttpClientFactory httpClientFactory, ILogger<FreeMyIpProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override DNSProviderKind Kind => DNSProviderKind.FreeMyIp;

    /// <inheritdoc />
    public override string Label => "freemyip.com";

    /// <inheritdoc />
    public override string Hint => TranslationKeys.DdnsProviderHintFreeMyIp;

    /// <inheritdoc />
    public override ProviderFields Fields => new()
    {
        Hostname = TranslationKeys.ProxyLabelDomain,
        Password = TranslationKeys.DdnsLabelToken,
        Server = true,
    };

    /// <inheritdoc />
    public override async Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(ip);

        if (string.IsNullOrWhiteSpace(record.Hostname) || string.IsNullOrWhiteSpace(record.Password))
        {
            return DNSUpdateResult.Fail("freemyip requires a domain (hostname) and a token (password).");
        }

        // freemyip updates both the A and AAAA records from the request when myip is omitted. Only pin a
        // single address when exactly one family is enabled, so dual-stack doesn't silently drop a family.
        var v4 = record.UpdateIPv4 ? ip.IPv4 : null;
        var v6 = record.UpdateIPv6 ? ip.IPv6 : null;
        var explicitIp = (v4 is not null && v6 is not null) ? null : (v4 ?? v6);

        var url = ServerBase(record, DefaultServer)
            + "/update?token=" + Uri.EscapeDataString(record.Password)
            + "&domain=" + Uri.EscapeDataString(record.Hostname);
        if (explicitIp is not null)
        {
            url += "&myip=" + Uri.EscapeDataString(explicitIp);
        }

        var result = await SendAsync(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return DNSUpdateResult.Fail("HTTP " + result.Status + ".");
        }

        // Match the status token, not a substring: the body "OK" must not be confused with words like
        // "TOKEN" that also contain "OK".
        return string.Equals(FirstToken(result.Body), "OK", StringComparison.OrdinalIgnoreCase)
            ? WithFamilies(DNSUpdateResult.Ok("freemyip accepted the update."), v4 is not null, v6 is not null)
            : DNSUpdateResult.Fail("server said: " + FirstLine(result.Body));
    }
}
