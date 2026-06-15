namespace Attendance.Domain.Entities;

public class Shift
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Time of day "HH:mm" (24h).</summary>
    public string ShiftStart { get; set; } = "10:00";

    /// <summary>Time of day "HH:mm" (24h).</summary>
    public string ShiftEnd { get; set; } = "19:00";

    public int RequiredMinutes { get; set; } = 480;

    public string LunchStart { get; set; } = "13:00";
    public string LunchEnd { get; set; } = "14:00";

    public bool AutoDeductLunch { get; set; } = true;
    public bool LunchPaid { get; set; } = false;

    public int GraceMinutes { get; set; } = 5;
    public int HalfDayThresholdMinutes { get; set; } = 240;

    /// <summary>0=Sunday .. 6=Saturday. Stored as comma-separated in DB via value converter.</summary>
    public List<int> WeeklyOffDays { get; set; } = new() { 0 };
}
