using System;

namespace Jellyfin.Plugin.Necto.Configuration.Groups;

/// <summary>
/// The Groups settings that are not permissions, in the Groups section of Settings.
/// </summary>
public class GroupSettings
{
    /// <summary>Gets or sets the group a new user joins, or <c>null</c> to leave them in Everyone.</summary>
    public Guid? NewUserGroupId { get; set; }
}
