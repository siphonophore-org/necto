using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may have in WireGuard.
/// </summary>
public class WireGuardPermissions
{
    /// <summary>Gets or sets a value indicating whether members may have peers.</summary>
    public bool MayHavePeers { get; set; }

    /// <summary>Gets or sets how many peers a member may have. 0 is no limit.</summary>
    public int MaxPeers { get; set; } = 1;

    /// <summary>Gets or sets how much data all of a member's peers may move together in a period, in bytes. 0 is no limit.</summary>
    public long DataLimit { get; set; }

    /// <summary>Gets or sets the period <see cref="DataLimit"/> counts over.</summary>
    public LimitPeriod DataPeriod { get; set; } = LimitPeriod.Month;
}
