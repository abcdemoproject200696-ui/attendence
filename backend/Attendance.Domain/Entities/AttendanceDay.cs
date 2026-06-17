using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

public class AttendanceDay
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>The calendar date (date-only; time component ignored).</summary>
    public DateTime Date { get; set; }

    public DateTime? FirstIn { get; set; }
    public DateTime? LastOut { get; set; }

    public int GrossMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int LunchDeduction { get; set; }

    /// <summary>
    /// The exact auto-deducted lunch window (for display only). NOT persisted — it is
    /// recomputed from punches every time the day is calculated, so no DB column / migration.
    /// </summary>
    [NotMapped] public DateTime? LunchFrom { get; set; }
    [NotMapped] public DateTime? LunchTo { get; set; }

    public int NetMinutes { get; set; }

    public DayStatus Status { get; set; }
    public bool HasOpenSession { get; set; }

    public bool IsManual { get; set; }
    public string? ManualNote { get; set; }
}
