using System;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Tasks.Proxy;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Proxy;

/// <summary>
/// The DDNS and Proxy services are registered, and the certificate task warns and notifies about certificates.
/// </summary>
public class ServicesTests
{
    /// <summary>
    /// Every provider, the update run, the proxy, its hosted services and the visitor history are registered.
    /// True: Jellyfin can build every DDNS and Proxy service.
    /// False: a route or task would fail to resolve at startup.
    /// </summary>
    [Fact]
    public void Register_AddsEveryDdnsAndProxyService()
    {
        var services = new ServiceCollection();
        DdnsServices.Register(services);
        ProxyServices.Register(services);

        Assert.True(services.Count(d => d.ServiceType == typeof(IDNSProvider)) >= 45);
        foreach (var type in new[] { typeof(DNSUpdateService), typeof(StatusStoreService), typeof(SecretProtector), typeof(ProxyManager), typeof(CertificateSet), typeof(BanList), typeof(VisitorStore), typeof(ActivitySummary) })
        {
            Assert.Contains(services, d => d.ServiceType == type);
        }

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
        Assert.Contains(typeof(ProxyHostedService), hosted);
        Assert.Contains(typeof(VisitorHostedService), hosted);
        Assert.Contains(typeof(ServerTextSetup), hosted);
    }

    /// <summary>
    /// A certificate that expires within two weeks sends proxy.certificate_expiring, one that cannot load sends
    /// proxy.certificate_failed, and a route with no certificate sends nothing.
    /// True: the groups that receive Proxy events hear before HTTPS stops working.
    /// False: a certificate would run out without a word.
    /// </summary>
    [Fact]
    public void RenewalTask_Review_NotifiesExpiringAndFailedCertificates()
    {
        var activity = new FakeActivity();
        var task = new CertificateRenewalTask(InterfaceStubProxy(), activity.Logger, new FakeNotifier(), NullLogger<CertificateRenewalTask>.Instance);
        var now = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
        var events = task.Review(new[]
        {
            new RouteStatus { Id = "a", Host = "media.example.com", Certificate = new CertificateStatus { Mode = CertificateMode.Automatic, Loaded = true, NotAfterUtc = now.AddDays(5), Names = { "media.example.com" } } },
            new RouteStatus { Id = "b", Host = "app.example.com", Certificate = new CertificateStatus { Mode = CertificateMode.PemFiles, Loaded = false, Error = "missing file" } },
            new RouteStatus { Id = "c", Host = "nas.example.com", Certificate = new CertificateStatus { Mode = CertificateMode.None } },
            new RouteStatus { Id = "d", Host = "ok.example.com", Certificate = new CertificateStatus { Mode = CertificateMode.Automatic, Loaded = true, NotAfterUtc = now.AddDays(60) } }
        }, now);

        Assert.Equal(new[] { ProxyEvents.CertificateExpiring, ProxyEvents.CertificateFailed }, events.Select(e => e.Key).ToArray());
        Assert.Equal(2, activity.Entries.Count);
    }

    // The task's Review never touches the proxy, so it is built without one.
    private static ProxyManager InterfaceStubProxy() => (ProxyManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProxyManager));
}
