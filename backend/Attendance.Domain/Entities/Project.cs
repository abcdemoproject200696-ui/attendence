using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

/// <summary>
/// A project groups Kanban tasks together. Persisted to the "Projects" table.
/// </summary>
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int CreatedById { get; set; }
    [ForeignKey(nameof(CreatedById))]
    public Employee? CreatedBy { get; set; }

    // Active | Hold | Inactive. Only "Active" projects appear in the task assign dropdown;
    // all (incl. Hold/Inactive) still show in the projects table.
    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
