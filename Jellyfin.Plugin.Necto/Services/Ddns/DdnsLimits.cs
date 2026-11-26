using System;
using Jellyfin.Plugin.Necto.Configuration.Ddns;

namespace Jellyfin.Plugin.Necto.Services.Ddns;

/// <summary>
/// The ranges the DDNS settings are held to, so the update run's time math can never overflow.
/// </summary>
public static class DdnsLimits
{
    /// <summary>The longest force update and back off interval, one year.</summary>
    public const int MaxIntervalHours = 8760;

    /// <summary>The longest time between a record's checks, one week.</summary>
    public const int MaxUpdateEveryMinutes = 10080;

    /// <summary>The shortest time between a record's checks, which is how often the update task runs.</summary>
    public const int MinUpdateEveryMinutes = 5;

    /// <summary>
    /// Brings the settings into the ranges the update run works with.
    /// </summary>
    /// <param name="settings">The settings, changed in place.</param>
    public static void Clamp(DdnsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ForceUpdateHours = Math.Clamp(settings.ForceUpdateHours, 0, MaxIntervalHours);
        settings.BackoffAfterFailures = Math.Max(0, settings.BackoffAfterFailures);
        settings.BackoffHours = Math.Clamp(settings.BackoffHours, 1, MaxIntervalHours);
        settings.RequestTimeoutSeconds = Math.Clamp(settings.RequestTimeoutSeconds, 1, 300);
        settings.IPv4DetectionUrl ??= string.Empty;
        settings.IPv6DetectionUrl ??= string.Empty;
    }

    /// <summary>
    /// Brings a record's update interval into range.
    /// </summary>
    /// <param name="record">The record, changed in place.</param>
    public static void Clamp(DNSRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.UpdateEveryMinutes = Math.Clamp(record.UpdateEveryMinutes, MinUpdateEveryMinutes, MaxUpdateEveryMinutes);
    }
}
