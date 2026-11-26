using System;
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
/// Covers DNS Made Easy's reply tokens: "success" and "error-record-ip-same" (no update required) are
/// both healthy outcomes, while an auth error is a failure. Treating the unchanged reply as failure
/// used to walk perfectly healthy records into backoff on force-interval pushes.
/// </summary>
public class DnsMadeEasyProviderTests
{
    private static DNSRecord Record() => new()
    {
        Hostname = "12345",
        Login = "user@example.com",
        Password = "recordpass",
        UpdateIPv4 = true,
        UpdateIPv6 = false
    };

    [Fact]
    public async Task SuccessReply_IsReportedAsSuccess()
    {
        var provider = new DnsMadeEasyProvider(
            StubHttp.Always(HttpStatusCode.OK, "success"),
            NullLogger<DnsMadeEasyProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task NoUpdateRequiredReply_IsReportedAsSuccess()
    {
        var provider = new DnsMadeEasyProvider(
            StubHttp.Always(HttpStatusCode.OK, "error-record-ip-same"),
            NullLogger<DnsMadeEasyProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("unchanged", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthErrorReply_IsReportedAsFailure()
    {
        var provider = new DnsMadeEasyProvider(
            StubHttp.Always(HttpStatusCode.OK, "error-auth"),
            NullLogger<DnsMadeEasyProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.False(result.Success);
    }
}
