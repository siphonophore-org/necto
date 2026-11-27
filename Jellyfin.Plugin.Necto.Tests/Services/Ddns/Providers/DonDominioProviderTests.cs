using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns.Providers;

/// <summary>
/// Covers DonDominio's plain-text protocol: success is the OK token (or IP:&lt;ip&gt;) on the LAST line
/// of the reply, so earlier lines and OK-lookalike words ("LOOKUP") must not read as success.
/// </summary>
public class DonDominioProviderTests
{
    private static DNSRecord Record() => new()
    {
        Hostname = "home.example.com",
        Login = "user",
        Password = "api-key",
        UpdateIPv4 = true,
    };

    [Theory]
    [InlineData("OK", true)]
    [InlineData("Bienvenido\nOK", true)] // last line carries the status
    [InlineData("IP:1.2.3.4 updated", true)] // alternate success shape: the new address echoed back
    [InlineData("KO - invalid user", false)]
    [InlineData("LOOKUP failed", false)] // contains "OK" as a substring but is not the token
    [InlineData("OK\nERROR", false)] // an OK on an earlier line must not pass
    public async Task ParsesLastLineOfReply(string body, bool expectSuccess)
    {
        var provider = new DonDominioProvider(
            StubHttp.Always(HttpStatusCode.OK, body),
            NullLogger<DonDominioProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal(expectSuccess, result.Success);
    }

    [Fact]
    public async Task HttpError_IsFailure()
    {
        var provider = new DonDominioProvider(
            StubHttp.Always(HttpStatusCode.Forbidden, "OK"),
            NullLogger<DonDominioProvider>.Instance);

        var result = await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RequestShape_SendsUserPasswordHostAndIp()
    {
        string? url = null;
        var factory = StubHttp.Factory(req =>
        {
            url = req.RequestUri!.AbsoluteUri;
            return (HttpStatusCode.OK, "OK");
        });
        var provider = new DonDominioProvider(factory, NullLogger<DonDominioProvider>.Instance);

        await provider.UpdateAsync(Record(), new DetectedIP { IPv4 = "1.2.3.4" }, CancellationToken.None);

        Assert.Equal(
            "https://dondns.dondominio.com/plain/?user=user&password=api-key&host=home.example.com&ip=1.2.3.4",
            url);
    }
}
