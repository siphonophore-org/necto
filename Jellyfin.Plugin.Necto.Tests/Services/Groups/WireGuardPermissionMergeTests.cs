using System;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.Groups;

/// <summary>
/// WireGuard permissions of several groups combine to the most generous, counting only groups that allow peers.
/// </summary>
public class WireGuardPermissionMergeTests
{
    /// <summary>
    /// The data limit of a member's peers together takes the largest allowing group's, with its period, and 0 beats any number.
    /// True: the most generous group decides.
    /// False: a member is held to the strictest group, or to one that does not allow peers.
    /// </summary>
    [Fact]
    public void Merge_DataLimit_TakesTheLargestAllowingGroup()
    {
        var user = Guid.NewGuid();
        var small = TestGroups.Make("Small", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.DataLimit = 10; p.WireGuard.DataPeriod = LimitPeriod.Day; }, user);
        var large = TestGroups.Make("Large", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.DataLimit = 50; p.WireGuard.DataPeriod = LimitPeriod.Week; }, user);
        var refused = TestGroups.Make("Off", p => { p.WireGuard.MayHavePeers = false; p.WireGuard.DataLimit = 0; }, user);
        var unlimited = TestGroups.Make("Open", p => { p.WireGuard.MayHavePeers = true; p.WireGuard.DataLimit = 0; }, user);

        var merged = PermissionMerger.Merge(user, new[] { small, large, refused }).WireGuard;
        Assert.Equal((50L, LimitPeriod.Week), (merged.DataLimit, merged.DataPeriod));

        Assert.Equal(0L, PermissionMerger.Merge(user, new[] { small, unlimited }).WireGuard.DataLimit);
    }
}
