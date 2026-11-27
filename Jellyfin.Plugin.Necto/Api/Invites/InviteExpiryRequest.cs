using System;

namespace Jellyfin.Plugin.Necto.Api.Invites;

/// <summary>
/// Moves an invite's day.
/// </summary>
public class InviteExpiryRequest
{
    /// <summary>Gets or sets the new day, or <c>null</c> for never.</summary>
    public DateTime? ExpiresAt { get; set; }
}
