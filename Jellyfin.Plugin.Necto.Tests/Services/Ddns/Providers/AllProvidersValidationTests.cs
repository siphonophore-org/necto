using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns.Providers;

/// <summary>
/// Every registered provider must reject a record that has no hostname or credentials before it makes any
/// network call. The HTTP factory throws if a client is ever created, so a provider that skips its guards
/// fails loudly and names itself.
/// </summary>
public class AllProvidersValidationTests
{
    [Fact]
    public async Task EveryProvider_RejectsEmptyRecord_WithoutNetwork()
    {
        var noNetwork = Stubs.NoHttp();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(noNetwork);
        foreach (var type in typeof(IDNSProvider).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDNSProvider).IsAssignableFrom(t)))
        {
            services.AddSingleton(typeof(IDNSProvider), type);
        }

        using var sp = services.BuildServiceProvider();
        var providers = sp.GetServices<IDNSProvider>().ToList();
        Assert.NotEmpty(providers);

        var ip = new DetectedIP { IPv4 = "1.2.3.4", IPv6 = "2001:db8::1" };
        foreach (var p in providers)
        {
            var empty = new DNSRecord { UpdateIPv4 = true, UpdateIPv6 = true };

            // Record.ExceptionAsync captures any failure without a catch-all clause of our own, so a
            // provider that reaches for the network before validating is reported as a test failure
            // rather than tearing down the run.
            DNSUpdateResult? result = null;
            var thrown = await Record.ExceptionAsync(async () =>
                result = await p.UpdateAsync(empty, ip, CancellationToken.None));

            if (thrown is not null)
            {
                throw new Xunit.Sdk.XunitException(p.Kind + " attempted a network call before validating: " + thrown.Message);
            }

            Assert.NotNull(result);
            Assert.False(result!.Success, p.Kind + " accepted an empty record");
        }
    }
}
