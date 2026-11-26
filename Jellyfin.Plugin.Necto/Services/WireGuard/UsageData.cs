using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>Each peer's and connection's traffic counts, as stored.</summary>
public sealed class UsageData
{
    /// <summary>Gets the counts by identifier, then by period name.</summary>
    public Dictionary<string, Dictionary<string, PeriodCount>> Counts { get; init; } = new(StringComparer.Ordinal);
}
