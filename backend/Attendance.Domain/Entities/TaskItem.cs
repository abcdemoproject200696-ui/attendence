using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

/// <summary>
/// A Kanban task. Named TaskItem (not Task) to avoid clashing with
/// System.Threading.Tasks.Task. Persisted to the "Tasks" table.
/// </summary>
public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int AssigneeId { get; set; }
    [ForeignKey(nameof(AssigneeId))]
    public Employee? Assignee { get; set; }

    public int AssignedById { get; set; }
    [ForeignKey(nameof(AssignedById))]
    public Employee? AssignedBy { get; set; }

    /// <summary>One of: "ToDo","InProgress","Review","Done".</summary>
    public string Status { get; set; } = "ToDo";

    /// <summary>One of: "Low","Medium","High","Urgent".</summary>
    public string Priority { get; set; } = "Medium";

    /// <summary>ISO date "yyyy-MM-dd" (nullable).</summary>
    public string? DueDate { get; set; }

    public int? ProjectId { get; set; }
    [ForeignKey(nameof(ProjectId))]
    public Project? Project { get; set; }

    /// <summary>ISO date-time "yyyy-MM-ddTHH:mm" (nullable).</summary>
    public string? StartTime { get; set; }

    /// <summary>ISO date-time "yyyy-MM-ddTHH:mm" (nullable).</summary>
    public string? EndTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
