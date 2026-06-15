namespace Attendance.Domain.Entities;

public class Holiday
{
    public int Id { get; set; }

    /// <summary>Calendar date (date-only).</summary>
    public DateTime Date { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; } = true;
}
