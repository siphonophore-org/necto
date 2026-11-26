namespace Jellyfin.Plugin.Necto.Services.WireGuard;

/// <summary>The bytes counted in one window of a period.</summary>
public sealed class PeriodCount
{
    /// <summary>Gets or sets the window, such as 2026-11 for a month.</summary>
    public string Window { get; set; } = string.Empty;

    /// <summary>Gets or sets the bytes sent and received in the window.</summary>
    public long Bytes { get; set; }
}
