using System;
using Jellyfin.Plugin.Necto.Configuration.Storage;

namespace Jellyfin.Plugin.Necto.Services.Storage;

/// <summary>
/// Decides whether a disk is too hot or worn out against the health alerts. Pure, so the rules are testable.
/// </summary>
public static class HealthLevels
{
    /// <summary>
    /// How far, in °C, a hot disk must cool below the limit before it clears. A disk's temperature moves a degree or
    /// two between checks, so without it a disk at the limit would be recorded again and again.
    /// </summary>
    public const int CoolMargin = 5;

    /// <summary>
    /// Decides whether a disk is too hot.
    /// </summary>
    /// <param name="celsius">The disk's temperature, or <c>null</c> when it reports none.</param>
    /// <param name="alerts">The health alerts.</param>
    /// <param name="wasHot">Whether it was too hot at the last check.</param>
    /// <returns><c>true</c> when it is too hot.</returns>
    public static bool IsHot(int? celsius, HealthAlerts alerts, bool wasHot)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        return alerts.Hot && celsius is { } at && at >= (wasHot ? alerts.HotCelsius - CoolMargin : alerts.HotCelsius);
    }

    /// <summary>
    /// Decides whether an SSD is worn out. Wear only grows, so it needs no margin.
    /// </summary>
    /// <param name="wearPercent">The share of its rated life used, or <c>null</c> when it reports none.</param>
    /// <param name="alerts">The health alerts.</param>
    /// <returns><c>true</c> when it is worn out.</returns>
    public static bool IsWorn(int? wearPercent, HealthAlerts alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        return alerts.Worn && wearPercent is { } used && used >= alerts.WornPercent;
    }
}
