using System;

namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// The date a group's members expire on, and what happens to them then. Only the date counts, never the time.
/// </summary>
public class ExpiryRule
{
    /// <summary>Gets or sets the date members expire on, or <c>null</c> for never.</summary>
    public DateTime? ExpiresOn { get; set; }

    /// <summary>Gets or sets what happens to members once the date comes.</summary>
    public ExpiryAction Action { get; set; } = ExpiryAction.Disable;
}
