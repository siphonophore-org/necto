using System.Collections.Generic;
using Jellyfin.Plugin.Necto.Configuration.WireGuard;

namespace Jellyfin.Plugin.Necto.Api.WireGuard;

/// <summary>
/// Every peer's and connection's traffic in the current window of each period, and what the tunnels leave out.
/// </summary>
public sealed class UsageResponse
{
    /// <summary>Gets or sets the bytes sent and received, by peer or connection id without dashes, then by period.</summary>
    public Dictionary<string, Dictionary<LimitPeriod, long>> Bytes { get; set; } = new();

    /// <summary>Gets or sets the peers and connections over their data limit.</summary>
    public List<string> Blocked { get; set; } = new();

    /// <summary>Gets or sets the peers their owners' groups leave out, by id without dashes, with why: NotAllowed or OverPeerCount.</summary>
    public Dictionary<string, string> Refused { get; set; } = new();
}
