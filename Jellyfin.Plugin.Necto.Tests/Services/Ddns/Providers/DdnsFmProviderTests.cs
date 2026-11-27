using System;
using System.Net;
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
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns.Providers;

/// <summary>
/// Covers DDNS.fm response validation: an HTTP 200 whose body carries an error token must be reported
/// as failure. Trusting the status code alone used to record a rejected update (bad key, unknown
/// domain) as a successful push that was never retried.
/// </summary>
public class DdnsFmProviderTests
{
    private static DNSRecord Record() => new()
    {
        Hostname = "home.example.com",
        Password = "updatekey",
        UpdateIPv4 = true,
        UpdateIPv6 = false
    };

    [Fact]
    public async Task GoodReply_IsReportedAsSuccess()
    {
        string? url = null;
        var factory = StubHttp.Factory(req =>
        {
            url = req.RequestUri!.ToString();
            return (HttpStatusCode.OK, "good 1.2.3.4");
        });
        var provider = new DdnsFmProvider(factory, NullLogger<DdnsFmProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("/update?key=updatekey&domain=home.example.com&myip=1.2.3.4", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NochgReply_IsReportedAsSuccess()
    {
        var provider = new DdnsFmProvider(
            StubHttp.Always(HttpStatusCode.OK, "nochg 1.2.3.4"),
            NullLogger<DdnsFmProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ErrorBodyWith200_IsReportedAsFailure()
    {
        var provider = new DdnsFmProvider(
            StubHttp.Always(HttpStatusCode.OK, "badauth"),
            NullLogger<DdnsFmProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("badauth", result.Message, StringComparison.Ordinal);
    }
}
