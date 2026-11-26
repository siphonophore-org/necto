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
/// Key-Systems (port of ddclient's <c>nic_keysystems_update</c>). Sends one
/// <c>server/update.php?hostname=&amp;password=&amp;ip=</c> GET per address and treats a body
/// containing <c>code = 200</c> as success. <see cref="DNSRecord.Hostname"/> is the host and
/// <see cref="DNSRecord.Password"/> is the service password. <see cref="DNSRecord.Server"/> overrides
/// the default endpoint.
/// </summary>
public sealed class KeySystemsProvider : DNSProviderBase
{
    private const string DefaultServer = "dynamicdns.key-systems.net";

    /// <summary>Initializes a new instance of the <see cref="KeySystemsProvider"/> class.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public KeySystemsProvider(IHttpClientFactory httpClientFactory, ILogger<KeySystemsProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override DNSProviderKind Kind => DNSProviderKind.KeySystems;

    /// <inheritdoc />
    public override string Label => "Key-Systems";

    /// <inheritdoc />
    public override string Hint => TranslationKeys.DdnsProviderHintKeySystems;

    /// <inheritdoc />
    public override ProviderFields Fields => new()
    {
        Hostname = TranslationKeys.LabelServerHost,
        Password = TranslationKeys.LabelPassword,
        Server = true,
    };

    /// <inheritdoc />
    public override async Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(ip);

        if (string.IsNullOrWhiteSpace(record.Hostname) || string.IsNullOrWhiteSpace(record.Password))
        {
            return DNSUpdateResult.Fail("Key-Systems requires a hostname and a password.");
        }

        var server = ServerBase(record, DefaultServer);

        return await ApplyPerFamilyAsync(
            record,
            ip,
            (type, address, ct) => PushAsync(server, record.Hostname, record.Password, address, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Message)> PushAsync(
        string server,
        string hostname,
        string password,
        string value,
        CancellationToken cancellationToken)
    {
        var url = server + "/update.php?hostname=" + Uri.EscapeDataString(hostname)
            + "&password=" + Uri.EscapeDataString(password)
            + "&ip=" + Uri.EscapeDataString(value);

        var result = await SendAsync(HttpMethod.Get, url, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            return (false, "HTTP " + result.Status);
        }

        return result.Body.Contains("code = 200", StringComparison.Ordinal)
            ? (true, "set to " + value)
            : (false, "failed (" + FirstLine(result.Body) + ")");
    }
}
