namespace Attendance.Domain;

/// <summary>Pure output of <see cref="AttendanceCalculator"/> for a single day.</summary>
public sealed class AttendanceCalculation
{
    public DateTime? FirstIn { get; init; }
    public DateTime? LastOut { get; init; }
    public int GrossMinutes { get; init; }
    public int BreakMinutes { get; init; }
    public int LunchDeduction { get; init; }
    public int NetMinutes { get; init; }
    public DayStatus Status { get; init; }
    public bool HasOpenSession { get; init; }
}
