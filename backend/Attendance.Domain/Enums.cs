namespace Attendance.Domain;

public enum Direction
{
    IN,
    OUT
}

public enum PunchSource
{
    Face,
    Code,
    Manual
}

public enum DayStatus
{
    Present,
    HalfDay,
    Absent,
    Holiday,
    Leave,
    WeeklyOff
}

public enum LeaveType
{
    Casual,
    Sick,
    Paid,
    Unpaid
}

public enum LeaveStatus
{
    Pending,
    Approved,
    Rejected
}
