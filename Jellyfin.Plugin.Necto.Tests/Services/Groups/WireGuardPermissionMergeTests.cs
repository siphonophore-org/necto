using System;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// WireGuard permissions come from a user's one group, and count only while it allows peers.
/// </summary>
public class WireGuardPermissionMergeTests
{
    /// <summary>
    /// The data limit of a member's peers together is the group's, with its period, while it allows peers. 0 is no limit.
    /// True: the group's limit holds.
    /// False: a member would be held to a limit of a group that does not allow peers, or lose the group's period.
    /// </summary>
    [Fact]
    public void Merge_DataLimit_TakesTheGroupsWhileItAllowsPeers()
    {
        var user = Guid.NewGuid();
        var large = TestGroups.Make("Large", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.DataLimit = 50; p.WireGuard.DataPeriod = LimitPeriod.Week; }, user);
        var refused = TestGroups.Make("Off", p => { p.WireGuard.MayHavePeers = false; p.WireGuard.DataLimit = 10; p.WireGuard.DataPeriod = LimitPeriod.Day; }, user);
        var unlimited = TestGroups.Make("Open", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.DataLimit = 0; p.WireGuard.DataPeriod = LimitPeriod.Day; }, user);

        var merged = PermissionMerger.Merge(user, large).WireGuard;
        Assert.Equal((50L, LimitPeriod.Week), (merged.DataLimit, merged.DataPeriod));

        var off = PermissionMerger.Merge(user, refused).WireGuard;
        Assert.Equal((0L, LimitPeriod.Month, 0), (off.DataLimit, off.DataPeriod, off.MaxPeers));
        var open = PermissionMerger.Merge(user, unlimited).WireGuard;
        Assert.Equal((0L, LimitPeriod.Month), (open.DataLimit, open.DataPeriod));
    }
}
