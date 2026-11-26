using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// What members may do in Invites.
/// </summary>
public class InvitePermissions
{
    /// <summary>Gets or sets a value indicating whether members may create invites.</summary>
    public bool MayCreate { get; set; }

    /// <summary>Gets or sets how many open invites a member may have. 0 is no limit.</summary>
    public int MaxInvites { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether members' invites may target any group.</summary>
    public bool AnyGroup { get; set; }

    /// <summary>Gets or sets the groups members' invites may target.</summary>
    public List<Guid> TargetGroupIds { get; set; } = new();
}
