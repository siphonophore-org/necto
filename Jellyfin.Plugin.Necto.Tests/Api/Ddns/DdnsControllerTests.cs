using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Configuration;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Siphonophore.Stolon;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Ddns;

/// <summary>
/// The DDNS routes keep credentials encrypted and with where they are sent, start a retargeted record's status over,
/// ignore status a page sends, clamp the settings, and never take the run off the request's abort token. A generic
/// settings save leaves the records alone.
/// </summary>
[Collection(PluginInstanceCollection.Name)]
public class DdnsControllerTests
{
    private const string Kept = SecretProtector.KeptSentinel;

    private static DdnsController Controller(OrchestratorHarness h, SecretProtector? secrets = null)
        => new(h.Service, h.Status, secrets ?? h.Secrets, h.ActivityLog, h.Providers);

    private static OrchestratorHarness WithInwx()
        => new(http => new IDNSProvider[] { new InwxProvider(http, NullLogger<InwxProvider>.Instance) });

    private static DNSRecord InwxRecord(string id = "inwx") => new()
    {
        Id = id,
        Name = id,
        Provider = DNSProviderKind.Inwx,
        Hostname = string.Empty,
        Login = "user",
        Password = "pass",
        UpdateIPv4 = true
    };

    private static RecordStatus PausedStatus() => new()
    {
        LastIPv4 = "198.51.100.1",
        LastSuccess = false,
        LastCheckedUtc = DateTime.UtcNow,
        ConsecutiveFailures = 3,
        BackoffUntilUtc = DateTime.UtcNow + TimeSpan.FromHours(20)
    };

    // What the page posts back: the listed record, with credentials as the kept marker, edited.
    private static DNSRecord Posted(DdnsController controller, string id, Action<DNSRecord> edit)
    {
        var list = ((OkObjectResult)controller.ListRecords().Result!).Value!;
        var json = System.Text.Json.JsonSerializer.Serialize(list);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var record = doc.RootElement.GetProperty("Records").EnumerateArray()
            .Select(e => System.Text.Json.JsonSerializer.Deserialize<DNSRecord>(e.GetProperty("Record").GetRawText())!)
            .Single(r => r.Id == id);
        edit(record);
        return record;
    }

    /// <summary>
    /// Each change that sends the record somewhere else starts its status over, and the other record keeps its own.
    /// True: a moved record never inherits the old target's pause and last address.
    /// False: a fixed record would stay paused for a day.
    /// </summary>
    [Theory]
    [InlineData("hostname")]
    [InlineData("provider")]
    [InlineData("zone")]
    [InlineData("server")]
    [InlineData("password")]
    [InlineData("follow")]
    public void UpdateRecord_RetargetingEdit_ClearsTheRecordsStatus(string field)
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord(), OrchestratorHarness.DynDnsRecord("other", "other.example.com"));
        h.SeedStatus("rec1", PausedStatus());
        h.SeedStatus("other", PausedStatus());
        var controller = Controller(h);

        var result = controller.UpdateRecord("rec1", Posted(controller, "rec1", r =>
        {
            switch (field)
            {
                case "hostname": r.Hostname = "new.example.com"; break;
                case "provider": r.Provider = DNSProviderKind.NoIp; break;
                case "zone": r.Zone = "example.com"; break;
                case "server": r.Server = "https://other.test"; break;
                case "follow": r.FollowProxyHosts = true; break;
                default: r.Password = "a-new-password"; break;
            }
        }));

        Assert.IsType<OkObjectResult>(result.Result);
        var records = h.Status.Load().Records;
        Assert.False(records.ContainsKey("rec1"));
        Assert.Equal(3, records["other"].ConsecutiveFailures);
    }

    /// <summary>
    /// A rename or turning a record off keeps its status.
    /// True: the page still shows why it is paused.
    /// False: renaming would wipe its history.
    /// </summary>
    [Fact]
    public void UpdateRecord_CosmeticEdit_KeepsTheRecordsStatus()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        h.SeedStatus("rec1", PausedStatus());
        var controller = Controller(h);

        controller.UpdateRecord("rec1", Posted(controller, "rec1", r =>
        {
            r.Name = "renamed";
            r.Enabled = false;
            r.UpdateEveryMinutes = 60;
        }));

        Assert.Equal(3, h.StatusOf("rec1").ConsecutiveFailures);
        Assert.Equal(60, h.Plugin.ReadConfiguration(c => c.Ddns.Records[0].UpdateEveryMinutes));
    }

    /// <summary>
    /// The kept marker keeps a stored credential, an empty field clears it, and a typed one is stored encrypted.
    /// True: credentials never reach the page and never sit on disk in the clear.
    /// False: a saved password would leak or be lost on every save.
    /// </summary>
    [Fact]
    public void UpdateRecord_KeptKeeps_EmptyClears_TypedIsEncrypted()
    {
        using var h = new OrchestratorHarness();
        var stored = OrchestratorHarness.DynDnsRecord();
        stored.Login = h.Secrets.Protect("user");
        stored.Password = h.Secrets.Protect("pass");
        h.SetRecords(stored);
        var controller = Controller(h);

        var posted = Posted(controller, "rec1", r => { r.Password = string.Empty; });
        Assert.Equal(Kept, posted.Login);
        controller.UpdateRecord("rec1", posted);
        var saved = h.Plugin.ReadConfiguration(c => c.Ddns.Records[0].Clone());
        Assert.Equal("user", h.Secrets.Unprotect(saved.Login));
        Assert.Equal(string.Empty, saved.Password);

        controller.UpdateRecord("rec1", Posted(controller, "rec1", r => r.Password = "typed"));
        saved = h.Plugin.ReadConfiguration(c => c.Ddns.Records[0].Clone());
        Assert.True(SecretProtector.IsProtected(saved.Password));
        Assert.Equal("typed", h.Secrets.Unprotect(saved.Password));
    }

    /// <summary>
    /// A kept credential does not follow a record moved to another server.
    /// True: an edit can never send a saved password somewhere new.
    /// False: pointing a record at another server would hand it the old password.
    /// </summary>
    [Fact]
    public void UpdateRecord_KeptCredentialOnAMovedRecord_IsDropped()
    {
        using var h = new OrchestratorHarness();
        var stored = OrchestratorHarness.DynDnsRecord();
        stored.Password = h.Secrets.Protect("pass");
        h.SetRecords(stored);
        var controller = Controller(h);

        controller.UpdateRecord("rec1", Posted(controller, "rec1", r => r.Server = "https://elsewhere.test"));

        Assert.Equal(string.Empty, h.Plugin.ReadConfiguration(c => c.Ddns.Records[0].Password));
    }

    /// <summary>
    /// Status the page sends with a record is dropped, so a stored provider reply cannot block a save.
    /// True: the status store is the only source of status.
    /// False: an odd reply echoed back would refuse every save.
    /// </summary>
    [Fact]
    public void UpdateRecord_ClientSentStatus_IsIgnored()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        var controller = Controller(h);

        var result = controller.UpdateRecord("rec1", Posted(controller, "rec1", r =>
        {
            r.LastStatus = "server said: ￾";
            r.LastAction = "Updated";
            r.ConsecutiveFailures = 99;
        }));

        var saved = (DNSRecord)Assert.IsType<OkObjectResult>(result.Result).Value!;
        Assert.Equal(string.Empty, saved.LastStatus);
        Assert.Equal(0, saved.ConsecutiveFailures);
        Assert.False(h.Status.Load().Records.ContainsKey("rec1"));
    }

    /// <summary>
    /// A provider that hides the hostname field saves without one, and one that shows it needs one.
    /// True: Inwx and the like work the host out from the account.
    /// False: those providers could never be saved, or others saved without a name.
    /// </summary>
    [Theory]
    [InlineData(DNSProviderKind.Inwx, false)]
    [InlineData(DNSProviderKind.DynDns2, true)]
    public void CreateRecord_HostnameIsRequiredOnlyWhereTheProviderShowsIt(DNSProviderKind kind, bool required)
    {
        using var h = WithInwx();
        var record = InwxRecord();
        record.Provider = kind;
        record.Hostname = "  ";

        var result = Controller(h).CreateRecord(record).Result;

        if (required)
        {
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Empty(h.Plugin.ReadConfiguration(c => c.Ddns.Records.ToList()));
        }
        else
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(string.Empty, h.Plugin.ReadConfiguration(c => c.Ddns.Records.Single().Hostname));
        }
    }

    /// <summary>
    /// A record that follows the proxy needs no hostname of its own.
    /// True: its names come from the routes.
    /// False: such a record could not be saved.
    /// </summary>
    [Fact]
    public void CreateRecord_FollowingTheProxy_NeedsNoHostname()
    {
        using var h = new OrchestratorHarness();
        var record = OrchestratorHarness.DynDnsRecord(host: string.Empty);
        record.FollowProxyHosts = true;
        Assert.IsType<OkObjectResult>(Controller(h).CreateRecord(record).Result);
    }

    /// <summary>
    /// Without the key store a typed credential is refused and nothing is stored, while a save with none typed
    /// still goes through.
    /// True: a credential is never stored in the clear.
    /// False: a broken key store would write passwords to disk as they are.
    /// </summary>
    [Fact]
    public void Save_WithoutTheKeyStore_RefusesATypedCredential()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        var before = h.Plugin.ReadConfiguration(c => c.Ddns.Records.Single().Password);
        var broken = new SecretProtector("broken", NullLogger.Instance);
        var controller = Controller(h, broken);

        var refused = Assert.IsType<BadRequestObjectResult>(controller.UpdateRecord("rec1", Posted(controller, "rec1", r => r.Password = "typed-in-the-clear")).Result);
        Assert.Contains("Reinstall the plugin", System.Text.Json.JsonSerializer.Serialize(refused.Value), StringComparison.Ordinal);
        Assert.Equal(before, h.Plugin.ReadConfiguration(c => c.Ddns.Records.Single().Password));

        Assert.IsType<OkObjectResult>(controller.UpdateRecord("rec1", Posted(controller, "rec1", r => r.Name = "renamed")).Result);
        Assert.Equal("renamed", h.Plugin.ReadConfiguration(c => c.Ddns.Records.Single().Name));
    }

    /// <summary>
    /// An encrypted value sent in a credential field is refused, since the page never sends one.
    /// True: a copied ciphertext cannot be stored against another record.
    /// False: a value from elsewhere would be kept as if typed.
    /// </summary>
    [Fact]
    public void CreateRecord_EncryptedCredential_IsRefused()
    {
        using var h = new OrchestratorHarness();
        var record = OrchestratorHarness.DynDnsRecord();
        record.Password = h.Secrets.Protect("pass");
        Assert.IsType<BadRequestObjectResult>(Controller(h).CreateRecord(record).Result);
    }

    /// <summary>
    /// Huge intervals are clamped to ones the run can add to a date, and the records stay as they are.
    /// True: no setting can overflow the update run.
    /// False: one large number would fail every run.
    /// </summary>
    [Fact]
    public void SaveSettings_ClampsHugeIntervals_AndKeepsTheRecords()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        var posted = new DdnsSettings { ForceUpdateHours = int.MaxValue, BackoffHours = int.MinValue, RequestTimeoutSeconds = int.MaxValue, Records = new List<DNSRecord>() };

        Assert.IsType<OkObjectResult>(Controller(h).SaveSettings(posted).Result);

        var saved = h.Plugin.ReadConfiguration(c => c.Ddns);
        Assert.Equal(DdnsLimits.MaxIntervalHours, saved.ForceUpdateHours);
        Assert.Equal(1, saved.BackoffHours);
        Assert.Equal(300, saved.RequestTimeoutSeconds);
        Assert.Single(saved.Records);
        _ = DateTime.UtcNow + TimeSpan.FromHours(saved.ForceUpdateHours);
    }

    /// <summary>
    /// Removing a record drops it and its status, and an unknown id is 404.
    /// True: the store does not keep status for records that are gone.
    /// False: removed records would linger in the status file.
    /// </summary>
    [Fact]
    public void RemoveRecord_DropsItAndItsStatus()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord(), OrchestratorHarness.DynDnsRecord("other", "other.example.com"));
        h.SeedStatus("rec1", PausedStatus());
        var controller = Controller(h);

        Assert.IsType<NoContentResult>(controller.RemoveRecord("rec1"));
        Assert.IsType<NotFoundObjectResult>(controller.RemoveRecord("rec1"));
        Assert.Equal(new[] { "other" }, h.Plugin.ReadConfiguration(c => c.Ddns.Records.Select(r => r.Id).ToList()));
        Assert.False(h.Status.Load().Records.ContainsKey("rec1"));
    }

    /// <summary>
    /// A following record lists the hosts it pushes for, from the internet routes under its zone.
    /// True: the page shows which names the record keeps current.
    /// False: the administrator could not tell what a following record updates.
    /// </summary>
    [Fact]
    public void ListRecords_FollowingRecord_ShowsItsHosts()
    {
        using var h = new OrchestratorHarness();
        var record = OrchestratorHarness.DynDnsRecord(host: string.Empty);
        record.FollowProxyHosts = true;
        record.Zone = "example.com";
        h.SetRecords(record);
        h.Plugin.MutateConfiguration(c =>
        {
            c.Proxy.Routes = TestRoutes.Of("media.example.com", "other.test", "*.example.com");
            c.Proxy.Routes.Add(new ProxyRoute { Id = "lan", Host = "home.example.com", LocalOnly = true });
            return false;
        });

        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)Controller(h).ListRecords().Result!).Value);

        Assert.Contains("media.example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("other.test", json, StringComparison.Ordinal);
        Assert.DoesNotContain("home.example.com", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Update now is not tied to the request's abort token.
    /// True: leaving the page mid run never forgets a push that landed.
    /// False: a push could land and be sent again.
    /// </summary>
    [Fact]
    public void UpdateNow_DoesNotTakeTheRequestAbortToken()
    {
        var method = typeof(DdnsController).GetMethod(nameof(DdnsController.UpdateNow), BindingFlags.Public | BindingFlags.Instance)!;
        Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(System.Threading.CancellationToken));
    }

    /// <summary>
    /// The generic settings save keeps the stored DDNS records and proxy routes, whatever it sends.
    /// True: only the tool routes, which encrypt and check, change them.
    /// False: a stale Settings page could write a typed password in the clear or bring back a removed record.
    /// </summary>
    [Fact]
    public void GenericSave_KeepsTheStoredRecordsAndRoutes()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        var config = new PluginConfiguration();
        config.Ddns.Records.Add(new DNSRecord { Id = "new", Hostname = "x.example.com", Password = "plain" });
        config.Proxy.Routes.Add(new ProxyRoute { Host = "media.example.com" });

        h.Plugin.UpdateConfiguration(config);

        Assert.Equal("rec1", h.Plugin.ReadConfiguration(c => c.Ddns.Records.Single().Id));
        Assert.Empty(h.Plugin.ReadConfiguration(c => c.Proxy.Routes.ToList()));
    }

    /// <summary>
    /// A provider reply with a character XML refuses is stored clean.
    /// True: the status store and the settings file stay readable.
    /// False: one odd reply could break the next start.
    /// </summary>
    [Fact]
    public async Task UpdateRun_ProviderReplyWithACharacterXmlRefuses_IsStoredClean()
    {
        using var h = new OrchestratorHarness();
        h.SetRecords(OrchestratorHarness.DynDnsRecord());
        h.DdnsReply = (HttpStatusCode.OK, "weird￾reply");

        var outcome = await h.RunAsync();

        Assert.Contains("weirdreply", outcome.Records.Single().Message, StringComparison.Ordinal);
        Assert.Null(XmlText.FindInvalid(new DNSRecord { LastStatus = h.StatusOf("rec1").LastStatus }));
    }

    /// <summary>
    /// The log sanitizer leaves nothing XML cannot hold and never splits a surrogate pair.
    /// True: a logged reply is always safe to store.
    /// False: a provider reply could corrupt the status file.
    /// </summary>
    [Fact]
    public void SanitizeForLog_LeavesNothingXmlCannotHold()
    {
        var text = FormatUtilities.SanitizeForLog("a\u0001b￾c\uD800d\uDC00e\r\nf", 100);
        Assert.Equal("abc�d�ef", text);
        Assert.Null(XmlText.FindInvalid(new DNSRecord { LastStatus = text }));
        var face = char.ConvertFromUtf32(0x1F600);
        Assert.Equal("ab" + face + "...", FormatUtilities.SanitizeForLog("ab" + face + "cd", 4));
    }
}
