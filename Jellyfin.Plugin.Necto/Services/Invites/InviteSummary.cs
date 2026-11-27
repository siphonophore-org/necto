using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.Invites;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// An invite as the Invites tool and a member's invite page show it, with its counters and without its PIN hash.
/// </summary>
public class InviteSummary
{
    /// <summary>Gets or sets the invite's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the token in the link.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Gets or sets the name shown in the Invites tool.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the welcome message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gets or sets the links shown to a new user.</summary>
    public List<InviteResource> Resources { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the invite asks for a PIN.</summary>
    public bool HasPin { get; set; }

    /// <summary>Gets or sets the group a new account joins, or <c>null</c> for the group set for new users.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Gets or sets the name of the group a new account joins today.</summary>
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Gets or sets the day the invite stops working, or <c>null</c> for never.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Gets or sets how many accounts the invite may make. 0 is no limit.</summary>
    public int MaxUses { get; set; }

    /// <summary>Gets or sets how many accounts the invite made.</summary>
    public int UsedCount { get; set; }

    /// <summary>Gets or sets a value indicating whether the invite is on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets where the invite stands.</summary>
    public InviteState State { get; set; }

    /// <summary>Gets or sets a value indicating whether the group a new account would join has expired, which expires the invite.</summary>
    public bool GroupExpired { get; set; }

    /// <summary>Gets or sets when the invite was made, in UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the user who made the invite, or <see cref="Guid.Empty"/> for the Invites tool.</summary>
    public Guid CreatedBy { get; set; }

    /// <summary>Gets or sets the name of the user who made the invite, or empty for the Invites tool.</summary>
    public string CreatedByName { get; set; } = string.Empty;
}
