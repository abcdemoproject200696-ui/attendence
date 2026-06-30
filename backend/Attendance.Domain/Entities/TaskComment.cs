using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

/// <summary>
/// A comment posted on a task by any employee. Body is Quill Delta JSON (rich
/// text that may embed small base64 images). AuthorName is denormalised (a
/// snapshot at post time) so listing comments needs no join. "TaskComments" table.
/// </summary>
public class TaskComment
{
    public int Id { get; set; }

    public int TaskId { get; set; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem? Task { get; set; }

    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>Rich-text body as Quill Delta JSON (can be large).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// When set, this comment is a threaded REPLY to another comment on the same
    /// task (Jira-style). Null = a top-level comment.
    /// </summary>
    public int? ParentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
