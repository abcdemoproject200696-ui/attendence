namespace Attendance.Domain.Entities;

public class LeaveRequest
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Inclusive start date (date-only).</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Inclusive end date (date-only).</summary>
    public DateTime ToDate { get; set; }

    public LeaveType Type { get; set; }
    public bool IsPaid { get; set; } = true;
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public string? Reason { get; set; }
}
