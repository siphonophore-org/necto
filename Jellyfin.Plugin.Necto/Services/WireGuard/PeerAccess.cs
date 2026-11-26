using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;
using Jellyfin.Plugin.Necto.Services.Groups;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>
/// Decides which peers their owners' groups allow. A peer with no owner, or one an administrator owns, is always
/// allowed. Any other owner needs a group that allows peers, and keeps only as many of their oldest enabled peers as
/// the most generous of those groups gives.
/// </summary>
public static class PeerAccess
{
    /// <summary>
    /// Finds the peers the tunnel leaves out because of their owner's groups.
    /// </summary>
    /// <param name="config">The WireGuard settings.</param>
    /// <param name="permissionsOf">Gives what a user may do.</param>
    /// <returns>Each peer left out, with why.</returns>
    public static Dictionary<Guid, PeerRefusal> Refused(WireGuardSettings config, Func<Guid, EffectivePermissions> permissionsOf)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(permissionsOf);
        var refused = new Dictionary<Guid, PeerRefusal>();
        foreach (var owner in config.Peers.Where(p => p.UserId != Guid.Empty).GroupBy(p => p.UserId))
        {
            var permissions = permissionsOf(owner.Key);
            if (permissions.IsAdministrator)
            {
                continue;
            }

            if (!permissions.WireGuard.MayHavePeers)
            {
                foreach (var peer in owner)
                {
                    refused[peer.Id] = PeerRefusal.NotAllowed;
                }

                continue;
            }

            var max = permissions.WireGuard.MaxPeers;
            if (max <= 0)
            {
                continue;
            }

            // The oldest peers keep working, so a peer added past the count is the one left out.
            foreach (var peer in owner.Where(p => p.Enabled).OrderBy(p => p.CreatedUtc).ThenBy(p => p.Id).Skip(max))
            {
                refused[peer.Id] = PeerRefusal.OverPeerCount;
            }
        }

        return refused;
    }

    /// <summary>
    /// Gives the data limit of all of a user's peers together, from their groups.
    /// </summary>
    /// <param name="permissions">What the user may do.</param>
    /// <returns>The limit in bytes, 0 for none, and its period.</returns>
    public static (long Limit, LimitPeriod Period) OwnerLimit(EffectivePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.IsAdministrator || !permissions.WireGuard.MayHavePeers
            ? (0, LimitPeriod.Month)
            : (Math.Max(0, permissions.WireGuard.DataLimit), permissions.WireGuard.DataPeriod);
    }
}
