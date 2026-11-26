namespace Jellyfin.Plugin.Necto.Configuration.WireGuard;

/// <summary>
/// The window a data limit counts over. Each starts at the top of the hour, midnight, Monday, the first of
/// the month, or the first of the year, in UTC.
/// </summary>
public enum LimitPeriod
{
    /// <summary>Each hour.</summary>
    Hour,

    /// <summary>Each day.</summary>
    Day,

    /// <summary>Each week, from Monday.</summary>
    Week,

    /// <summary>Each month.</summary>
    Month,

    /// <summary>Each year.</summary>
    Year
}
