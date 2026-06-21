using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

/// <summary>
/// A file attached to a task. The file bytes are stored as a base64 string
/// (can be large text). Persisted to the "TaskAttachments" table.
/// </summary>
public class TaskAttachment
{
    public int Id { get; set; }

    public int TaskId { get; set; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem? Task { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;

    /// <summary>The file bytes as a base64 string (can be large).</summary>
    public string DataBase64 { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
