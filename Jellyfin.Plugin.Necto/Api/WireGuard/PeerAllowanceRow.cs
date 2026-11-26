using System;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Api.WireGuard;

/// <summary>
/// One user's peers and what their groups give them in WireGuard.
/// </summary>
public sealed class PeerAllowanceRow
{
    /// <summary>Gets or sets the user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the user is an administrator, who may have any number of peers.</summary>
    public bool IsAdministrator { get; set; }

    /// <summary>Gets or sets how many peers the user has.</summary>
    public int Used { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's groups allow peers.</summary>
    public bool MayHavePeers { get; set; }

    /// <summary>Gets or sets how many peers the groups give. 0 is no limit.</summary>
    public int MaxPeers { get; set; }

    /// <summary>Gets or sets how much data all of the user's peers may move together, in bytes. 0 is no limit.</summary>
    public long DataLimit { get; set; }

    /// <summary>Gets or sets the period the data limit counts over.</summary>
    public LimitPeriod DataPeriod { get; set; }
}
