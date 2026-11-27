using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Notifications;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// Each record keeps its own update interval, a record may follow the proxy's host names, and a failing record
/// sends ddns.failed to Notifications.
/// </summary>
[Collection(PluginInstanceCollection.Name)]
public class ScheduleAndEventsTests
{
    /// <summary>
    /// A record is due once its interval has passed since its last check, and a record never checked is due at once.
    /// True: a record set to an hour is pushed hourly while the task runs every few minutes.
    /// False: every record would be checked on every task run.
    /// </summary>
    [Fact]
    public void IsDue_FollowsTheRecordsInterval()
    {
        var now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(DNSUpdateService.IsDue(null, 60, now));
        Assert.False(DNSUpdateService.IsDue(new RecordStatus { LastCheckedUtc = now.AddMinutes(-30) }, 60, now));
        Assert.True(DNSUpdateService.IsDue(new RecordStatus { LastCheckedUtc = now.AddMinutes(-60) }, 60, now));
        Assert.True(DNSUpdateService.IsDue(new RecordStatus { LastCheckedUtc = now.AddSeconds(-280) }, 5, now));
    }

    /// <summary>
    /// A scheduled run checks only the records that are due, while Update now checks every one.
    /// True: the per record interval holds, and Update now still runs everything at once.
    /// False: records would be pushed on every five minute run.
    /// </summary>
    [Fact]
    public async Task ScheduledRun_ChecksOnlyTheRecordsThatAreDue()
    {
        using var h = new OrchestratorHarness();
        var hourly = OrchestratorHarness.DynDnsRecord("hourly", "a.example.com");
        hourly.UpdateEveryMinutes = 60;
        var often = OrchestratorHarness.DynDnsRecord("often", "b.example.com");
        often.UpdateEveryMinutes = 5;
        h.SetRecords(hourly, often);
        h.SeedStatus("hourly", new RecordStatus { LastCheckedUtc = DateTime.UtcNow.AddMinutes(-10) });
        h.SeedStatus("often", new RecordStatus { LastCheckedUtc = DateTime.UtcNow.AddMinutes(-10) });

        var scheduled = await h.RunScheduledAsync();
        Assert.Equal(new[] { "often" }, scheduled.Records.Select(r => r.Id).ToArray());

        var forced = await h.RunAsync();
        Assert.Equal(2, forced.Records.Count);
    }

    /// <summary>
    /// A record that follows the proxy is pushed once per internet route under its zone, each with its own status.
    /// True: every name the proxy answers for points at this server.
    /// False: a new route's name would never be published.
    /// </summary>
    [Fact]
    public async Task FollowingRecord_PushesEachInternetRouteUnderItsZone()
    {
        using var h = new OrchestratorHarness();
        var record = OrchestratorHarness.DynDnsRecord("f", string.Empty);
        record.FollowProxyHosts = true;
        record.Zone = "example.com";
        h.SetRecords(record);
        h.Plugin.MutateConfiguration(c =>
        {
            c.Proxy.Routes = TestRoutes.Of("media.example.com", "app.example.com", "*.example.com", "other.test");
            c.Proxy.Routes.Add(new ProxyRoute { Id = "lan", Host = "home.example.com", LocalOnly = true });
            return false;
        });

        var outcome = await h.RunAsync();

        Assert.Equal(new[] { "media.example.com", "app.example.com" }, outcome.Records.Select(r => r.Hostname).ToArray());
        Assert.Equal(2, h.DdnsCalls);
        Assert.NotNull(h.StatusOf("f" + RecordExpansion.Separator + "media.example.com").LastCheckedUtc);
    }

    /// <summary>
    /// A failed push sends ddns.failed, and the push that works again sends ddns.recovered.
    /// True: the groups that receive DDNS events hear about a name that may point at an old address.
    /// False: a broken record would go unnoticed until someone cannot connect.
    /// </summary>
    [Fact]
    public async Task FailedPush_SendsTheFailedEvent_AndRecoverySendsRecovered()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        h.DdnsReply = (HttpStatusCode.OK, "badauth");

        await h.RunAsync();
        var failed = Assert.Single(h.Notifier.Sent, e => e.Key == DdnsEvents.Failed);
        Assert.Equal(NectoEventLevel.Error, failed.Level);
        Assert.Contains("home.example.com", failed.Title, StringComparison.Ordinal);

        h.DdnsReply = (HttpStatusCode.OK, "good");
        h.PublicIPv4 = "198.51.100.11";
        await h.RunAsync();
        Assert.Contains(DdnsEvents.Recovered, h.Notifier.Keys);
    }

    /// <summary>
    /// Every event key the tools send is listed once, starts with its tool's key, and has a name.
    /// True: Notifications can list every DDNS and Proxy event.
    /// False: an event could be sent that no group can choose.
    /// </summary>
    [Fact]
    public void EventLists_NameEveryKeyTheToolsSend()
    {
        Assert.Contains(DdnsEvents.Failed, DdnsEvents.All.Select(x => x.Key));
        Assert.Equal("ddns.failed", DdnsEvents.Failed);
        Assert.All(DdnsEvents.All.Select(x => x.Key), k => Assert.StartsWith("ddns.", k, StringComparison.Ordinal));
        Assert.Equal("proxy.banned", ProxyEvents.Banned);
        Assert.Equal("proxy.certificate_expiring", ProxyEvents.CertificateExpiring);
        Assert.All(ProxyEvents.All.Select(x => x.Key), k => Assert.StartsWith("proxy.", k, StringComparison.Ordinal));
        Assert.All(DdnsEvents.All.Select(x => x.NameKey).Concat(ProxyEvents.All.Select(x => x.NameKey)), name => Assert.NotEqual(name, Siphonophore.Stolon.StolonTranslations.Get(name)));
    }

    /// <summary>
    /// Following records stand for one record per covered host, and a record with no zone covers every internet host.
    /// True: the expansion matches what the DDNS page lists.
    /// False: status would be kept under ids the page never shows.
    /// </summary>
    [Fact]
    public void Expansion_CoversTheHostsUnderTheZone()
    {
        var proxy = new ProxySettings { Routes = TestRoutes.Of("media.example.com", "a.test") };
        var follow = new DNSRecord { Id = "f", FollowProxyHosts = true };
        Assert.Equal(new[] { "f~media.example.com", "f~a.test" }, RecordExpansion.Expand(new[] { follow }, proxy).Select(r => r.Id).ToArray());
        follow.Zone = "example.com";
        Assert.Equal(new[] { "media.example.com" }, RecordExpansion.Expand(new[] { follow }, proxy).Select(r => r.Hostname).ToArray());
        Assert.Equal("f", RecordExpansion.SourceId("f~media.example.com"));
        Assert.Empty(RecordExpansion.Expand(new[] { follow }, null));
    }
}
