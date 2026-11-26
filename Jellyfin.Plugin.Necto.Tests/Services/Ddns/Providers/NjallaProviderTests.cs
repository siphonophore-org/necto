using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Hosting;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Api.Proxy;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns.Providers;

/// <summary>
/// Njalla sets A and AAAA in one GET to <c>/update/</c> and answers HTTP 200 with a JSON envelope
/// whose <c>status</c>/<c>message</c> carry the real outcome, so a 200 with an error envelope must
/// still fail. Pins that parsing plus the single-request query shape and the <c>&amp;auto</c> fallback.
/// </summary>
public class NjallaProviderTests
{
    private static DNSRecord Record() => new()
    {
        Hostname = "home.example.com",
        Password = "ddnskey",
        UpdateIPv4 = true,
    };

    [Theory]
    [InlineData("{\"status\":200,\"message\":\"record updated\",\"value\":{\"A\":\"1.2.3.4\"}}", true)]
    [InlineData("{\"status\":401,\"message\":\"Invalid host or key\"}", false)]
    [InlineData("{\"status\":200,\"message\":\"something unexpected\"}", false)]
    [InlineData("not json at all", false)]
    public async Task ParsesJsonEnvelope(string body, bool expectSuccess)
    {
        var provider = new NjallaProvider(StubHttp.Always(HttpStatusCode.OK, body), NullLogger<NjallaProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal(expectSuccess, result.Success);
    }

    [Fact]
    public async Task BothFamilies_GoOutInOneRequest()
    {
        var requests = 0;
        Uri? uri = null;
        var factory = StubHttp.Factory(req =>
        {
            requests++;
            uri = req.RequestUri;
            return (HttpStatusCode.OK, "{\"status\":200,\"message\":\"record updated\"}");
        });
        var provider = new NjallaProvider(factory, NullLogger<NjallaProvider>.Instance);
        var record = Record();
        record.UpdateIPv6 = true;

        var result = await provider.UpdateAsync(
            record,
            new DetectedIP { IPv4 = "1.2.3.4", IPv6 = "2001:db8::1" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, requests);
        Assert.Equal("njal.la", uri!.Host);
        Assert.Equal("/update/", uri.AbsolutePath);
        Assert.Contains("h=home.example.com", uri.Query, StringComparison.Ordinal);
        Assert.Contains("k=ddnskey", uri.Query, StringComparison.Ordinal);
        Assert.Contains("a=1.2.3.4", uri.Query, StringComparison.Ordinal);
        Assert.Contains("aaaa=2001%3Adb8%3A%3A1", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoDetectedAddress_FallsBackToAuto()
    {
        Uri? uri = null;
        var factory = StubHttp.Factory(req =>
        {
            uri = req.RequestUri;
            return (HttpStatusCode.OK, "{\"status\":200,\"message\":\"record updated\"}");
        });
        var provider = new NjallaProvider(factory, NullLogger<NjallaProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("&auto", uri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&a=", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&aaaa=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingKey_FailsWithoutNetwork()
    {
        var provider = new NjallaProvider(
            StubHttp.Factory(_ => throw new Xunit.Sdk.XunitException("network")),
            NullLogger<NjallaProvider>.Instance);
        var record = Record();
        record.Password = string.Empty;

        var result = await provider.UpdateAsync(record, new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.False(result.Success);
    }
}
