namespace Attendance.Domain;

/// <summary>
/// Per-day context that affects status precedence (holiday / weekly-off / approved leave).
/// </summary>
public sealed class DayContext
{
    public bool IsHoliday { get; init; }
    public bool IsApprovedLeave { get; init; }

    /// <summary>True if the day-of-week is in the shift's weekly-off set. Computed by caller.</summary>
    public bool IsWeeklyOff { get; init; }
}
