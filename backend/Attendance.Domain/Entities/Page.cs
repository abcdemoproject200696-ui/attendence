namespace Attendance.Domain.Entities;

public class Page
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int MenuOrder { get; set; }
}
