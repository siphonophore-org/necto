using System;
using System.IO;
using MediaBrowser.Common.Configuration;
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

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// Covers the runtime status store: it round trips status to disk and returns an empty status when no
/// file exists yet, so the dashboard and the update pass never depend on the config XML for status.
/// </summary>
public class StatusStoreServiceTests
{
    private static (StatusStoreService Store, string Dir) NewStore()
    {
        var dir = Path.Join(Path.GetTempPath(), "remoteaccess-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var paths = Stubs.Paths(dir);
        return (new StatusStoreService(paths, NullLogger<StatusStoreService>.Instance), dir);
    }

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        var (store, dir) = NewStore();
        try
        {
            var data = store.Load();
            Assert.NotNull(data);
            Assert.Empty(data.Records);
            Assert.Equal(string.Empty, data.DetectedIPv4);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var (store, dir) = NewStore();
        try
        {
            var data = new StatusData { DetectedIPv4 = "203.0.114.5", DetectionMessage = "note" };
            data.Records["rec1"] = new RecordStatus { LastAction = "Updated", LastSuccess = true, ConsecutiveFailures = 2 };
            store.Save(data);

            var loaded = store.Load();
            Assert.Equal("203.0.114.5", loaded.DetectedIPv4);
            Assert.Equal("note", loaded.DetectionMessage);
            Assert.True(loaded.Records.TryGetValue("rec1", out var rec1));
            Assert.Equal("Updated", rec1!.LastAction);
            Assert.Equal(2, rec1.ConsecutiveFailures);

            // The store file is the JSON file beside the keys, not the config XML.
            Assert.True(File.Exists(Path.Join(dir, "Jellyfin.Plugin.Necto.Ddns.json")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>Covers removing chosen records' status, as a retargeting save does.</summary>
public class StatusStoreRemoveTests
{
    [Fact]
    public void RemoveRecords_DropsOnlyTheNamedRecords()
    {
        var dir = Path.Join(Path.GetTempPath(), "remoteaccess-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = Stubs.Paths(dir);
            var store = new StatusStoreService(paths, NullLogger<StatusStoreService>.Instance);
            var data = new StatusData { DetectedIPv4 = "1.2.3.4" };
            data.Records["a"] = new RecordStatus { LastIPv4 = "1.1.1.1" };
            data.Records["b"] = new RecordStatus { LastIPv4 = "2.2.2.2" };
            store.Save(data);

            store.RemoveRecords(new[] { "a", "missing" });

            var loaded = store.Load();
            Assert.False(loaded.Records.ContainsKey("a"));
            Assert.Equal("2.2.2.2", loaded.Records["b"].LastIPv4);
            Assert.Equal("1.2.3.4", loaded.DetectedIPv4);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void StatusWrittenBeforeNewFields_LoadsWithDefaults()
    {
        var dir = Path.Join(Path.GetTempPath(), "remoteaccess-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Join(dir, "Jellyfin.Plugin.Necto.Ddns.json"),
                "{\"DetectedIPv4\":\"1.2.3.4\",\"Records\":{\"a\":{\"LastIPv4\":\"1.2.3.4\",\"ConsecutiveFailures\":3,\"BackoffUntilUtc\":\"2030-01-01T00:00:00Z\"}}}");
            var paths = Stubs.Paths(dir);

            var record = new StatusStoreService(paths, NullLogger<StatusStoreService>.Instance).Load().Records["a"];

            Assert.Equal(3, record.ConsecutiveFailures);
            Assert.Equal(0, record.TransientFailures);
            Assert.Equal(string.Empty, record.FailedIPv4);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
