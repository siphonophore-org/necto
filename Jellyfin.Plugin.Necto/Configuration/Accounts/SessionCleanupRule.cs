using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// One session cleanup rule: the clients it covers and how many days a device may go without checking in before it is
/// logged out. Where several rules of one group cover a client, the shortest window wins.
/// </summary>
public class SessionCleanupRule
{
    /// <summary>Gets or sets which clients the rule covers.</summary>
    public SessionClientMode Mode { get; set; } = SessionClientMode.All;

    /// <summary>Gets or sets the client names the mode lists, matched whatever their case.</summary>
    public List<string> Clients { get; set; } = new();

    /// <summary>Gets or sets how many days without checking in log a device out.</summary>
    public int Days { get; set; } = 30;
}
