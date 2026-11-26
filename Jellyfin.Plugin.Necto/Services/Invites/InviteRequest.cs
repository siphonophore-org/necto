using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Invites;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// What a new or changed invite holds, as the Invites tool or a member's invite page sends it.
/// </summary>
public class InviteRequest
{
    /// <summary>Gets or sets the name shown in the Invites tool.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the welcome message the signup page shows.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the links shown to a new user once the account is made.</summary>
    public List<InviteResource> Resources { get; set; } = new();

    /// <summary>Gets or sets the PIN, six digits, or empty for none. A change leaves the PIN as it is while this is <c>null</c>.</summary>
    public string? Pin { get; set; }

    /// <summary>Gets or sets the group a new account joins, or <c>null</c> for the group set for new users.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Gets or sets the day the invite stops working, or <c>null</c> for never.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Gets or sets how many accounts the invite may make. 0 is no limit.</summary>
    public int MaxUses { get; set; }
}
