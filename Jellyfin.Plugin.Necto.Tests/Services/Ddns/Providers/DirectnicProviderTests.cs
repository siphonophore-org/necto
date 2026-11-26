using System.Net;
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
/// Covers Directnic: the per-family gateway URL (Login for A, Password for AAAA) gets the address as a
/// <c>data</c> parameter, and only a JSON body with <c>result:"success"</c> counts as success.
/// </summary>
public class DirectnicProviderTests
{
    private const string GatewayUrl = "https://directnic.com/dns/gateway/abc123/";

    private static DNSRecord Record() => new()
    {
        Login = GatewayUrl,
        UpdateIPv4 = true,
    };

    [Theory]
    [InlineData("{\"result\":\"success\",\"message\":\"Your record has been updated.\"}", true)]
    [InlineData("{\"result\":\"error\",\"message\":\"invalid token\"}", false)]
    [InlineData("{\"message\":\"no result field\"}", false)]
    [InlineData("<html>not json</html>", false)] // 200 with a non-JSON body
    public async Task ParsesResultField(string body, bool expectSuccess)
    {
        var provider = new DirectnicProvider(
            StubHttp.Always(HttpStatusCode.OK, body),
            NullLogger<DirectnicProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal(expectSuccess, result.Success);
    }

    [Fact]
    public async Task RequestShape_AppendsDataParameterToGatewayUrl()
    {
        string? url = null;
        var factory = StubHttp.Factory(req =>
        {
            url = req.RequestUri!.AbsoluteUri;
            return (HttpStatusCode.OK, "{\"result\":\"success\"}");
        });
        var provider = new DirectnicProvider(factory, NullLogger<DirectnicProvider>.Instance);

        await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal(GatewayUrl + "?data=1.2.3.4", url);
    }

    [Fact]
    public async Task BareGatewayPath_IsAnchoredToTheDefaultHost()
    {
        string? url = null;
        var factory = StubHttp.Factory(req =>
        {
            url = req.RequestUri!.AbsoluteUri;
            return (HttpStatusCode.OK, "{\"result\":\"success\"}");
        });
        var provider = new DirectnicProvider(factory, NullLogger<DirectnicProvider>.Instance);
        var record = Record();
        record.Login = "dns/gateway/abc123";

        await provider.UpdateAsync(record, new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal("https://directnic.com/dns/gateway/abc123?data=1.2.3.4", url);
    }

    [Fact]
    public async Task IPv6WithoutGatewayUrl_FailsWithoutNetwork()
    {
        var provider = new DirectnicProvider(
            StubHttp.Factory(_ => throw new Xunit.Sdk.XunitException("network")),
            NullLogger<DirectnicProvider>.Instance);
        var record = new DNSRecord { Login = GatewayUrl, UpdateIPv4 = false, UpdateIPv6 = true };

        var result = await provider.UpdateAsync(record, new DetectedIP { IPv6 = "2001:db8::1" }, CancellationToken.None);

        Assert.False(result.Success);
    }
}
