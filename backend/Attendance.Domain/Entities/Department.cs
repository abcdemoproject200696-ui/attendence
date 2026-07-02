namespace Attendance.Domain.Entities;

/// <summary>A company department (master list for the Add-Employee dropdown).
/// The chosen department NAME is stored on the employee (Employee.Department).</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
