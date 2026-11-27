using System;
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

namespace Jellyfin.Plugin.Necto.Tests.Services.Ddns;

/// <summary>
/// Covers the runtime status snapshot that the store persists and the dashboard reads: reading a record's
/// status and applying it back must round trip every field so nothing is lost across a save or a run.
/// </summary>
public class RecordStatusTests
{
    [Fact]
    public void FromRecord_ThenApplyTo_RoundTripsEveryField()
    {
        var when = new DateTime(2026, 6, 8, 12, 0, 0, DateTimeKind.Utc);
        var source = new DNSRecord
        {
            LastIPv4 = "1.2.3.4",
            LastIPv6 = "2001:db8::1",
            LastStatus = "Updated 1.2.3.4",
            LastAction = "Updated",
            LastSuccess = true,
            LastCheckedUtc = when,
            LastUpdateUtc = when.AddMinutes(-5),
            ConsecutiveFailures = 3,
            BackoffUntilUtc = when.AddHours(24)
        };

        var target = new DNSRecord();
        RecordStatus.FromRecord(source).ApplyTo(target);

        Assert.Equal(source.LastIPv4, target.LastIPv4);
        Assert.Equal(source.LastIPv6, target.LastIPv6);
        Assert.Equal(source.LastStatus, target.LastStatus);
        Assert.Equal(source.LastAction, target.LastAction);
        Assert.Equal(source.LastSuccess, target.LastSuccess);
        Assert.Equal(source.LastCheckedUtc, target.LastCheckedUtc);
        Assert.Equal(source.LastUpdateUtc, target.LastUpdateUtc);
        Assert.Equal(source.ConsecutiveFailures, target.ConsecutiveFailures);
        Assert.Equal(source.BackoffUntilUtc, target.BackoffUntilUtc);
    }
}
