using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Siphonophore.Stolon;
using MediaBrowser.Model.Activity;
using Microsoft.Extensions.Logging;
using Xunit;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Proxy;
using Jellyfin.Plugin.Necto.Services.Ddns;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers;
using Jellyfin.Plugin.Necto.Services.Ddns.Providers.Implementations;
using Jellyfin.Plugin.Necto.Services.Proxy;
using Jellyfin.Plugin.Necto.Services.Proxy.Acme;
using Jellyfin.Plugin.Necto.Services.Proxy.Network;
using Jellyfin.Plugin.Necto.Services.Proxy.Reachability;
using Jellyfin.Plugin.Necto.Services.Proxy.Visitors;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Tests.Fakes;

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// Covers the configuration save audit: an added or removed record and a changed credential each get one
/// activity log entry, while an unchanged save stays silent. Credentials are compared in stored form, so
/// an echoed back stored value is not a change.
/// </summary>
public class ConfigurationAuditTests
{
    private readonly List<ActivityLog> _entries = new();
    private readonly ActivityLogger _activity;

    public ConfigurationAuditTests()
    {
                _activity = Stubs.Activity(_entries.Add);
    }

    private static DNSRecord Record(string id, string host = "host.example.com", string login = "enc:l", string password = "enc:p")
        => new() { Id = id, Hostname = host, Login = login, Password = password };

    [Fact]
    public void UnchangedSave_LogsNothing()
    {
        var records = new List<DNSRecord> { Record("a") };

        ConfigurationAudit.LogRecordChanges(_activity, records, new List<DNSRecord> { Record("a") });

        Assert.Empty(_entries);
    }

    [Fact]
    public void EditedRecord_LogsChanged_WithTheAdmin()
    {
        var admin = System.Guid.NewGuid();
        ConfigurationAudit.LogRecordChanges(_activity, new List<DNSRecord> { Record("a") }, new List<DNSRecord> { Record("a", host: "new.example.com") }, admin);

        var entry = Assert.Single(_entries);
        Assert.Equal("Necto.Ddns.RecordChanged", entry.Type);
        Assert.Equal(admin, entry.UserId);
    }

    [Fact]
    public void NewRecord_LogsAdded()
    {
        ConfigurationAudit.LogRecordChanges(_activity, new List<DNSRecord>(), new List<DNSRecord> { Record("a") });

        var entry = Assert.Single(_entries);
        Assert.Equal("Necto.Ddns.RecordAdded", entry.Type);
        Assert.Equal("DNS record added", entry.Name);
        Assert.Equal("An administrator added host.example.com with Cloudflare", entry.ShortOverview);
    }

    [Fact]
    public void DroppedRecord_LogsRemoved()
    {
        ConfigurationAudit.LogRecordChanges(_activity, new List<DNSRecord> { Record("a") }, new List<DNSRecord>());

        var entry = Assert.Single(_entries);
        Assert.Equal("Necto.Ddns.RecordRemoved", entry.Type);
    }

    [Fact]
    public void ChangedPassword_LogsCredentialsChanged_Once()
    {
        var before = new List<DNSRecord> { Record("a") };
        var after = new List<DNSRecord> { Record("a", password: "enc:replaced") };

        ConfigurationAudit.LogRecordChanges(_activity, before, after);

        var entry = Assert.Single(_entries);
        Assert.Equal("Necto.Ddns.RecordCredentialsChanged", entry.Type);
    }

    [Fact]
    public void ChangedLogin_LogsCredentialsChanged()
    {
        ConfigurationAudit.LogRecordChanges(
            _activity,
            new List<DNSRecord> { Record("a") },
            new List<DNSRecord> { Record("a", login: "enc:replaced") });

        Assert.Equal("Necto.Ddns.RecordCredentialsChanged", Assert.Single(_entries).Type);
    }

    [Fact]
    public void FirstSave_NullBefore_SumsEveryRecordAsAdded()
    {
        ConfigurationAudit.LogRecordChanges(_activity, null, new List<DNSRecord> { Record("a"), Record("b", host: "other.example.com") });

        var entry = Assert.Single(_entries);
        Assert.Equal("Necto.Ddns.RecordAdded", entry.Type);
        Assert.Equal("DNS records added", entry.Name);
        Assert.Equal("An administrator added host.example.com, other.example.com with Cloudflare (2)", entry.ShortOverview);
    }

    [Fact]
    public void ChangedAndRemoved_ReadAsSummaries_AndCredentialsStandAlone()
    {
        var admin = System.Guid.NewGuid();
        var before = new List<DNSRecord> { Record("a"), Record("b", host: "b.example.com"), Record("gone", host: "gone.example.com") };
        var after = new List<DNSRecord>
        {
            Record("a", host: "a2.example.com"),
            new() { Id = "b", Hostname = "b.example.com", Login = "enc:l", Password = "enc:new", Enabled = false }
        };

        ConfigurationAudit.LogRecordChanges(_activity, before, after, admin, "viewer");

        var changed = Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordChanged");
        Assert.Equal("DNS records changed", changed.Name);
        Assert.Equal("viewer changed a2.example.com, b.example.com, now off, on", changed.ShortOverview);
        Assert.Equal(admin, changed.UserId);

        var removed = Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordRemoved");
        Assert.Equal("DNS record removed", removed.Name);
        Assert.Equal("viewer removed gone.example.com from Cloudflare", removed.ShortOverview);

        var credentials = Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordCredentialsChanged");
        Assert.Equal("Credentials changed", credentials.Name);
        Assert.Equal("viewer changed the login or password of the Cloudflare DNS record for b.example.com", credentials.ShortOverview);
    }

    [Fact]
    public void MixedSave_LogsEachChangeSeparately()
    {
        var before = new List<DNSRecord> { Record("keep"), Record("drop"), Record("cred") };
        var after = new List<DNSRecord> { Record("keep"), Record("cred", password: "enc:new"), Record("fresh") };

        ConfigurationAudit.LogRecordChanges(_activity, before, after);

        Assert.Equal(3, _entries.Count);
        Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordAdded");
        Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordRemoved");
        Assert.Single(_entries, e => e.Type == "Necto.Ddns.RecordCredentialsChanged");
    }

    [Fact]
    public void NullActivityLogger_DoesNotThrow()
    {
        ConfigurationAudit.LogRecordChanges(null, new List<DNSRecord> { Record("a") }, new List<DNSRecord>());
    }
}
