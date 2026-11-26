using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// The invite counters file, by invite id.
/// </summary>
public class InviteStatusData
{
    /// <summary>Gets or sets each invite's counters.</summary>
    public Dictionary<Guid, InviteStatus> Invites { get; set; } = new();
}
