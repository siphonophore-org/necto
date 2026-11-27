using System;
using Jellyfin.Plugin.Necto.Api.Ddns;
using Jellyfin.Plugin.Necto.Configuration.Ddns;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Api.Ddns;

/// <summary>
/// A DDNS record's row state follows its last run, or the worst host it covers, and reads Off while it is off.
/// </summary>
public class RecordRowTests
{
    private static DNSRecord Record(string action, bool enabled = true, bool follows = false)
        => new() { Id = "r", Enabled = enabled, LastAction = action, FollowProxyHosts = follows };

    /// <summary>
    /// Each last run gives its state.
    /// True: the table's filter finds a record by how it stands.
    /// False: a failed record could hide under Up to date.
    /// </summary>
    [Theory]
    [InlineData("Updated", RecordRow.UpToDate)]
    [InlineData("IP Unchanged", RecordRow.UpToDate)]
    [InlineData("Failed", RecordRow.Attention)]
    [InlineData("Backing off", RecordRow.Attention)]
    [InlineData("No address", RecordRow.Attention)]
    [InlineData("", RecordRow.NotChecked)]
    public void StateOf_FollowsTheLastRun(string action, string state)
    {
        Assert.Equal(state, RecordRow.StateOf(Record(action), Array.Empty<DNSRecord>()));
    }

    /// <summary>
    /// A record that is off reads Off whatever its last run, and one that follows the proxy goes by its hosts.
    /// True: one failing host marks the whole record.
    /// False: a following record would read as its own empty last run.
    /// </summary>
    [Fact]
    public void StateOf_OffAndFollowedHosts()
    {
        Assert.Equal(RecordRow.Off, RecordRow.StateOf(Record("Failed", enabled: false), Array.Empty<DNSRecord>()));
        var follows = Record(string.Empty, follows: true);
        Assert.Equal(RecordRow.Attention, RecordRow.StateOf(follows, new[] { Record("Updated"), Record("Failed") }));
        Assert.Equal(RecordRow.UpToDate, RecordRow.StateOf(follows, new[] { Record("Updated"), Record("IP Unchanged") }));
        Assert.Equal(RecordRow.NotChecked, RecordRow.StateOf(follows, new[] { Record("Updated"), Record(string.Empty) }));
        Assert.Equal(RecordRow.NotChecked, RecordRow.StateOf(follows, Array.Empty<DNSRecord>()));
    }
}
