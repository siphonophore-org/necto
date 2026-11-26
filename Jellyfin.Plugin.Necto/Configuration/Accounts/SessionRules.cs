using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Configuration.Accounts;

/// <summary>
/// Logs out members' devices that go too long without checking in, by the clients each rule covers.
/// </summary>
public class SessionRules
{
    /// <summary>Gets or sets a value indicating whether members' stale devices are logged out.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the rules, each a set of clients and a window.</summary>
    public List<SessionCleanupRule> Rules { get; set; } = new();
}
