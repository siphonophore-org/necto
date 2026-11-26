using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Groups;
using Jellyfin.Plugin.Necto.Services.WireGuard;
using Xunit;

namespace Jellyfin.Plugin.Necto.Tests.Services.WireGuard;

/// <summary>
/// The tunnel keeps only the peers their owners' groups allow, up to the count the groups give.
/// </summary>
public class PeerAccessTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static EffectivePermissions Allow(Guid user, int max, long data = 0)
        => new() { UserId = user, WireGuard = new() { MayHavePeers = true, MaxPeers = max, DataLimit = data, DataPeriod = LimitPeriod.Week } };

    private static TunnelPeer Peer(Guid owner, int minute, bool enabled = true)
        => new() { UserId = owner, Name = "p" + minute, CreatedUtc = Start.AddMinutes(minute), Enabled = enabled };

    /// <summary>
    /// A member whose groups do not allow peers has every peer left out.
    /// True: taking a member out of the group cuts their peers off.
    /// False: a member keeps a tunnel their groups no longer give.
    /// </summary>
    [Fact]
    public void Refused_OwnerNotAllowed_LeavesEveryPeerOut()
    {
        var owner = Guid.NewGuid();
        var config = new WireGuardSettings { Peers = { Peer(owner, 0), Peer(owner, 1) } };

        var refused = PeerAccess.Refused(config, id => new EffectivePermissions { UserId = id });

        Assert.Equal(2, refused.Count);
        Assert.All(refused.Values, r => Assert.Equal(PeerRefusal.NotAllowed, r));
    }

    /// <summary>
    /// A member over their count keeps their oldest enabled peers and loses the newest, and a disabled peer does not count.
    /// True: adding one peer too many never cuts off a device that works.
    /// False: an old device stops working when a new one is added, or a disabled peer uses up the count.
    /// </summary>
    [Fact]
    public void Refused_OverCount_LeavesTheNewestOut()
    {
        var owner = Guid.NewGuid();
        var first = Peer(owner, 0);
        var off = Peer(owner, 1, enabled: false);
        var second = Peer(owner, 2);
        var third = Peer(owner, 3);
        var config = new WireGuardSettings { Peers = { third, first, off, second } };

        var refused = PeerAccess.Refused(config, id => Allow(id, 2));

        Assert.Equal(new Dictionary<Guid, PeerRefusal> { [third.Id] = PeerRefusal.OverPeerCount }, refused);
    }

    /// <summary>
    /// An administrator, a peer with no owner, and a member with no count limit keep every peer.
    /// True: an administrator's own devices always connect.
    /// False: an unowned or administrator peer is cut off.
    /// </summary>
    [Fact]
    public void Refused_AdministratorUnownedAndUnlimited_KeepEverything()
    {
        var admin = Guid.NewGuid();
        var member = Guid.NewGuid();
        var config = new WireGuardSettings { Peers = { Peer(Guid.Empty, 0), Peer(admin, 1), Peer(member, 2), Peer(member, 3) } };

        var refused = PeerAccess.Refused(config, id => id == admin ? PermissionMerger.Administrator(id) : Allow(id, 0));

        Assert.Empty(refused);
    }

    /// <summary>
    /// The data limit of a member's peers together comes from their groups, and none for an administrator.
    /// True: a group's Data per member applies to its members.
    /// False: a limit applies to administrators, or to no one.
    /// </summary>
    [Fact]
    public void OwnerLimit_ComesFromTheGroups()
    {
        var user = Guid.NewGuid();

        Assert.Equal((5_000L, LimitPeriod.Week), PeerAccess.OwnerLimit(Allow(user, 1, 5_000)));
        Assert.Equal(0L, PeerAccess.OwnerLimit(PermissionMerger.Administrator(user)).Limit);
        Assert.Equal(0L, PeerAccess.OwnerLimit(new EffectivePermissions()).Limit);
    }
}
