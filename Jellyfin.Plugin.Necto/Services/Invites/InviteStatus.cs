using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.Invites;

/// <summary>
/// An invite's counters, kept in their own file so a signup or a wrong PIN never rewrites the configuration.
/// </summary>
public class InviteStatus
{
    /// <summary>Gets or sets how many accounts the invite has made.</summary>
    public int UsedCount { get; set; }

    /// <summary>Gets or sets how many wrong PINs came in a row since the last signup.</summary>
    public int FailedPinAttempts { get; set; }

    /// <summary>Gets or sets when recent signups happened, in UTC, for the rate limit.</summary>
    public List<DateTime> RecentRedemptions { get; set; } = new();
}
