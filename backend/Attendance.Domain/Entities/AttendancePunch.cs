namespace Attendance.Domain.Entities;

public class AttendancePunch
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime Timestamp { get; set; }
    public Direction Direction { get; set; }

    public string? DeviceId { get; set; }
    public PunchSource Source { get; set; }
    public string? Note { get; set; }
}
