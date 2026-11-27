using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Proxy.Acme;

/// <summary>
/// The TXT records the Cloudflare, Duck DNS, DigitalOcean, and Porkbun providers publish and remove for DNS-01.
/// </summary>
public class AcmeDnsProviderTests
{
    [Fact]
    public async Task Cloudflare_CreatesThenDeletesOnlyItsOwnTxtRecord()
    {
        var calls = new List<string>();
        var bodies = new List<string>();
        var http = StubHttp.Factory(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            calls.Add(req.Method + " " + url);
            if (req.Content is not null)
            {
                bodies.Add(req.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }

            if (url.Contains("/zones/?name=", System.StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, "{\"result\":[{\"id\":\"zone1\",\"name\":\"example.com\"}]}");
            }

            if (req.Method == HttpMethod.Post)
            {
                return (HttpStatusCode.OK, "{\"result\":{\"id\":\"txt1\"}}");
            }

            if (req.Method == HttpMethod.Get && url.Contains("type=TXT", System.StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, "{\"result\":[{\"id\":\"txt1\",\"content\":\"\\\"token-value\\\"\"},{\"id\":\"other\",\"content\":\"someone-else\"}]}");
            }

            return (HttpStatusCode.OK, "{\"result\":{}}");
        });
        var provider = new CloudflareProvider(http, NullLogger<CloudflareProvider>.Instance);
        var record = new DNSRecord { Hostname = "home.example.com", Zone = "example.com", Login = "token", Password = "secret" };

        var (ok, _) = await provider.SetTxtAsync(record, "_acme-challenge.media.example.com", "token-value", CancellationToken.None);
        Assert.True(ok);
        Assert.Contains("\"type\":\"TXT\"", bodies[0], System.StringComparison.Ordinal);
        Assert.Contains("\"name\":\"_acme-challenge.media.example.com\"", bodies[0], System.StringComparison.Ordinal);
        Assert.Contains("\"content\":\"token-value\"", bodies[0], System.StringComparison.Ordinal);

        await provider.RemoveTxtAsync(record, "_acme-challenge.media.example.com", "token-value", CancellationToken.None);
        Assert.Contains(calls, c => c.StartsWith("DELETE ", System.StringComparison.Ordinal) && c.EndsWith("/dns_records/txt1", System.StringComparison.Ordinal));
        Assert.DoesNotContain(calls, c => c.EndsWith("/dns_records/other", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cloudflare_ReportsATokenWithoutEditRights()
    {
        var http = StubHttp.Factory(req => req.Method == HttpMethod.Post
            ? (HttpStatusCode.Forbidden, "{\"success\":false}")
            : (HttpStatusCode.OK, "{\"result\":[{\"id\":\"zone1\",\"name\":\"example.com\"}]}"));
        var provider = new CloudflareProvider(http, NullLogger<CloudflareProvider>.Instance);

        var (ok, message) = await provider.SetTxtAsync(new DNSRecord { Zone = "example.com", Login = "token", Password = "s" }, "_acme-challenge.example.com", "v", CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("DNS edit permission", message, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuckDns_SetsAndClearsTheDomainsTxtValue()
    {
        var urls = new List<string>();
        var http = StubHttp.Factory(req =>
        {
            urls.Add(req.RequestUri!.AbsoluteUri);
            return (HttpStatusCode.OK, "OK");
        });
        var provider = new DuckDnsProvider(http, NullLogger<DuckDnsProvider>.Instance);
        var record = new DNSRecord { Hostname = "myhome", Password = "tok" };

        var (ok, _) = await provider.SetTxtAsync(record, "_acme-challenge.myhome.duckdns.org", "abc", CancellationToken.None);
        Assert.True(ok);
        Assert.Equal("https://www.duckdns.org/update?domains=myhome&token=tok&txt=abc", urls[0]);

        await provider.RemoveTxtAsync(record, "_acme-challenge.myhome.duckdns.org", "abc", CancellationToken.None);
        Assert.Equal("https://www.duckdns.org/update?domains=myhome&token=tok&txt=&clear=true", urls[1]);
    }
    /// <summary>
    /// DigitalOcean posts the TXT record by its name in the zone, and removes only the one with its value.
    /// True: a DigitalOcean DDNS record can prove a wildcard.
    /// False: the record would land under the wrong name, or another client's TXT record would go.
    /// </summary>
    [Fact]
    public async Task DigitalOcean_CreatesThenDeletesOnlyItsOwnTxtRecord()
    {
        var calls = new List<string>();
        var bodies = new List<string>();
        var http = StubHttp.Factory(req =>
        {
            calls.Add(req.Method + " " + req.RequestUri!.AbsoluteUri);
            if (req.Content is not null)
            {
                bodies.Add(req.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }

            return req.Method == HttpMethod.Get
                ? (HttpStatusCode.OK, "{\"domain_records\":[{\"id\":11,\"data\":\"token-value\"},{\"id\":12,\"data\":\"other\"}]}")
                : (HttpStatusCode.Created, "{\"domain_record\":{\"id\":11}}");
        });
        var provider = new DigitalOceanProvider(http, NullLogger<DigitalOceanProvider>.Instance);
        var record = new DNSRecord { Hostname = "home", Zone = "example.com", Password = "token" };

        var (ok, _) = await provider.SetTxtAsync(record, "_acme-challenge.example.com", "token-value", CancellationToken.None);
        Assert.True(ok);
        Assert.Contains("\"name\":\"_acme-challenge\"", bodies[0], System.StringComparison.Ordinal);
        Assert.Contains("\"data\":\"token-value\"", bodies[0], System.StringComparison.Ordinal);
        Assert.Contains(calls, c => c == "POST https://api.digitalocean.com/v2/domains/example.com/records");

        Assert.True(await provider.RemoveTxtAsync(record, "_acme-challenge.example.com", "token-value", CancellationToken.None));
        Assert.Contains(calls, c => c == "DELETE https://api.digitalocean.com/v2/domains/example.com/records/11");
        Assert.DoesNotContain(calls, c => c.EndsWith("/records/12", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Porkbun creates the TXT record under the root domain, refuses without one, and removes only its own value.
    /// True: a Porkbun DDNS record can prove a wildcard, and says what is missing when it cannot.
    /// False: the record would land under the wrong name, or another client's TXT record would go.
    /// </summary>
    [Fact]
    public async Task Porkbun_CreatesThenDeletesOnlyItsOwnTxtRecord()
    {
        var calls = new List<string>();
        var bodies = new List<string>();
        var http = StubHttp.Factory(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            calls.Add(url);
            bodies.Add(req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            return url.Contains("retrieveByNameType", System.StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "{\"status\":\"SUCCESS\",\"records\":[{\"id\":\"7\",\"content\":\"token-value\"},{\"id\":\"8\",\"content\":\"other\"}]}")
                : (HttpStatusCode.OK, "{\"status\":\"SUCCESS\",\"id\":7}");
        });
        var provider = new PorkbunProvider(http, NullLogger<PorkbunProvider>.Instance);
        var record = new DNSRecord { Hostname = "home.example.com", Zone = "example.com", Login = "pk", Password = "sk" };

        var (ok, _) = await provider.SetTxtAsync(record, "_acme-challenge.media.example.com", "token-value", CancellationToken.None);
        Assert.True(ok);
        Assert.Equal("https://api.porkbun.com/api/json/v3/dns/create/example.com", calls[0]);
        Assert.Contains("\"name\":\"_acme-challenge.media\"", bodies[0], System.StringComparison.Ordinal);
        Assert.Contains("\"type\":\"TXT\"", bodies[0], System.StringComparison.Ordinal);

        Assert.True(await provider.RemoveTxtAsync(record, "_acme-challenge.media.example.com", "token-value", CancellationToken.None));
        Assert.Contains(calls, c => c.EndsWith("/dns/delete/example.com/7", System.StringComparison.Ordinal));
        Assert.DoesNotContain(calls, c => c.EndsWith("/dns/delete/example.com/8", System.StringComparison.Ordinal));

        var (missing, message) = await provider.SetTxtAsync(new DNSRecord { Login = "pk", Password = "sk" }, "_acme-challenge.example.com", "v", CancellationToken.None);
        Assert.False(missing);
        Assert.Contains("root domain", message, System.StringComparison.Ordinal);
    }
}
