using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Siphonophore.Stolon;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;

/// <summary>
/// DigitalOcean DNS API v2. Port of ddclient's <c>nic_digitalocean_update</c>: authenticates with a
/// Bearer personal access token, looks up the existing A/AAAA record by name, and PATCHes its data when
/// the IP has changed. Set password to the DigitalOcean API token and zone to the apex domain
/// (e.g. <c>example.com</c>). Hostname is the record name. It also publishes and removes the TXT records of a
/// DNS-01 challenge in the zone.
/// </summary>
public sealed class DigitalOceanProvider : DNSProviderBase, IAcmeDnsProvider
{
    private const string DefaultServer = "api.digitalocean.com";

    /// <summary>Initializes a new instance of the <see cref="DigitalOceanProvider"/> class.</summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public DigitalOceanProvider(IHttpClientFactory httpClientFactory, ILogger<DigitalOceanProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    /// <inheritdoc />
    public override DNSProviderKind Kind => DNSProviderKind.DigitalOcean;

    /// <inheritdoc />
    public override string Label => "DigitalOcean";

    /// <inheritdoc />
    public override string Hint => TranslationKeys.DdnsProviderHintDigitalOcean;

    /// <inheritdoc />
    public override ProviderFields Fields => new()
    {
        Hostname = TranslationKeys.DdnsLabelRecordName,
        Password = TranslationKeys.DdnsLabelApiToken,
        Zone = TranslationKeys.DdnsLabelApexDomain,
        Server = true,
    };

    /// <inheritdoc />
    public override async Task<DNSUpdateResult> UpdateAsync(DNSRecord record, DetectedIP ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(ip);

        if (string.IsNullOrWhiteSpace(record.Password))
        {
            return DNSUpdateResult.Fail("DigitalOcean requires an API token (password).");
        }

        if (string.IsNullOrWhiteSpace(record.Zone))
        {
            return DNSUpdateResult.Fail("DigitalOcean requires a zone (apex domain).");
        }

        if (string.IsNullOrWhiteSpace(record.Hostname))
        {
            return DNSUpdateResult.Fail("DigitalOcean requires a hostname.");
        }

        var server = ServerBase(record, DefaultServer);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Authorization", "Bearer " + record.Password),
        };

        // A bare label is completed with the zone, since the name filter matches the full record name.
        var host = QualifiedName(record.Hostname, record.Zone);
        return await ApplyPerFamilyAsync(
            record,
            ip,
            (type, address, ct) => UpdateOneAsync(server, headers, record, host, type, address, ct),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(bool Ok, string Message)> SetTxtAsync(DNSRecord record, string name, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Password) || string.IsNullOrWhiteSpace(record.Zone))
        {
            return (false, "DigitalOcean needs an API token and a zone to publish the TXT record.");
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["type"] = "TXT", ["name"] = RelativeName(name, record.Zone), ["data"] = value, ["ttl"] = 60 });
        var create = await SendAsync(HttpMethod.Post, RecordsUrl(record), cancellationToken, Auth(record), body).ConfigureAwait(false);
        return create.Ok
            ? (true, "published")
            : (false, "DigitalOcean did not create the TXT record (HTTP " + create.Status.ToString(CultureInfo.InvariantCulture) + "). The API token needs write access.");
    }

    /// <inheritdoc />
    public async Task<bool> RemoveTxtAsync(DNSRecord record, string name, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var list = await SendAsync(HttpMethod.Get, RecordsUrl(record) + "?type=TXT&name=" + Uri.EscapeDataString(name), cancellationToken, Auth(record)).ConfigureAwait(false);
        if (!list.Ok)
        {
            return false;
        }

        // Only the record with this value is removed, so another client's TXT record of the same name stays.
        var removed = true;
        foreach (var id in TxtIds(list.Body, value))
        {
            var delete = await SendAsync(HttpMethod.Delete, RecordsUrl(record) + "/" + id, cancellationToken, Auth(record)).ConfigureAwait(false);
            removed &= delete.Ok || delete.Status == 404;
        }

        return removed;
    }

    // The zone's records address.
    private string RecordsUrl(DNSRecord record)
        => ServerBase(record, DefaultServer) + "/v2/domains/" + Uri.EscapeDataString(record.Zone.Trim().TrimEnd('.')) + "/records";

    private static List<KeyValuePair<string, string>> Auth(DNSRecord record)
        => new() { new("Authorization", "Bearer " + record.Password) };

    // DigitalOcean names a record by its part in front of the zone.
    private static string RelativeName(string name, string zone)
    {
        var domain = "." + zone.Trim().TrimEnd('.');
        return name.EndsWith(domain, StringComparison.OrdinalIgnoreCase) ? name[..^domain.Length] : name;
    }

    private static List<string> TxtIds(string body, string value)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("domain_records", out var records) || records.ValueKind != JsonValueKind.Array)
            {
                return ids;
            }

            foreach (var item in records.EnumerateArray())
            {
                if (item.TryGetProperty("data", out var data) && string.Equals(data.GetString()?.Trim('"'), value, StringComparison.Ordinal)
                    && item.TryGetProperty("id", out var id))
                {
                    ids.Add(id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString() ?? string.Empty);
                }
            }
        }
        catch (JsonException)
        {
            // Nothing to remove.
        }

        return ids.FindAll(id => id.Length > 0);
    }

    private async Task<(bool Ok, string Message)> UpdateOneAsync(
        string server,
        List<KeyValuePair<string, string>> headers,
        DNSRecord record,
        string host,
        string type,
        string ipValue,
        CancellationToken cancellationToken)
    {
        var listUrl = string.Concat(
            server,
            "/v2/domains/",
            Uri.EscapeDataString(record.Zone),
            "/records?name=",
            Uri.EscapeDataString(host),
            "&type=",
            type);

        var list = await SendAsync(HttpMethod.Get, listUrl, cancellationToken, headers).ConfigureAwait(false);
        if (!list.Ok)
        {
            return (false, "listing failed (HTTP " + list.Status.ToString(CultureInfo.InvariantCulture) + ")");
        }

        if (!TryGetSingleRecord(list.Body, out var recordId, out var currentIp))
        {
            return (false, "listing failed (no record, multiple records, or malformed JSON)");
        }

        if (string.Equals(currentIp, ipValue, StringComparison.Ordinal))
        {
            return (true, "already " + ipValue + ", no update needed");
        }

        var updateUrl = string.Concat(
            server,
            "/v2/domains/",
            Uri.EscapeDataString(record.Zone),
            "/records/",
            recordId);

        var body = "{\"type\":\"" + type + "\",\"data\":\"" + ipValue + "\"}";
        var update = await SendAsync(HttpMethod.Patch, updateUrl, cancellationToken, headers, body: body).ConfigureAwait(false);

        return update.Ok
            ? (true, "set to " + ipValue)
            : (false, "update failed (HTTP " + update.Status.ToString(CultureInfo.InvariantCulture) + ")");
    }

    private static bool TryGetSingleRecord(string body, out string recordId, out string currentIp)
    {
        recordId = string.Empty;
        currentIp = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("domain_records", out var records)
                || records.ValueKind != JsonValueKind.Array
                || records.GetArrayLength() != 1)
            {
                return false;
            }

            var elem = records[0];
            if (elem.ValueKind != JsonValueKind.Object
                || !elem.TryGetProperty("id", out var id)
                || !elem.TryGetProperty("data", out var data))
            {
                return false;
            }

            recordId = id.ValueKind == JsonValueKind.Number
                ? id.GetRawText()
                : id.GetString() ?? string.Empty;
            currentIp = data.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(recordId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
