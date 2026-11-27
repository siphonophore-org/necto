namespace Jellyfin.Plugin.Necto.Configuration.Policies;

/// <summary>
/// One access schedule window, as Jellyfin's <c>AccessSchedule</c> holds it. The day or days, and the hours in which
/// access is allowed.
/// </summary>
public class AccessScheduleEntry
{
    /// <summary>Gets or sets the day of week, a <c>DynamicDayOfWeek</c> name such as Everyday, Weekday, or Sunday.</summary>
    public string DayOfWeek { get; set; } = "Everyday";

    /// <summary>Gets or sets the start hour from 0 to 24, where a fraction is part of an hour.</summary>
    public double StartHour { get; set; }

    /// <summary>Gets or sets the end hour from 0 to 24, where a fraction is part of an hour.</summary>
    public double EndHour { get; set; }
}
