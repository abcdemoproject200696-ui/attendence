using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
public class CommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public CommentsController(AppDbContext db) => _db = db;

    /// <summary>List a task's comments, oldest first.</summary>
    [HttpGet("api/tasks/{taskId:int}/comments")]
    public async Task<ActionResult<IEnumerable<TaskCommentDto>>> ListForTask(int taskId)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId))
            return NotFound($"Task {taskId} does not exist.");

        var list = await _db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.Id)
            .ToListAsync();
        return Ok(list.Select(c => c.ToDto()));
    }

    /// <summary>Post a comment to a task. AuthorName is snapshotted from the employee.</summary>
    [HttpPost("api/tasks/{taskId:int}/comments")]
    public async Task<ActionResult<TaskCommentDto>> Add(int taskId, TaskCommentInputDto dto)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId))
            return BadRequest($"Task {taskId} does not exist.");
        if (string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest("Comment cannot be empty.");
        // Rich text may embed base64 images; cap so the free DB can't be flooded.
        if (dto.Body.Length > 12_000_000)
            return BadRequest("Comment is too large. Please use smaller images.");

        // Threaded reply: the parent comment must exist AND belong to this task.
        if (dto.ParentId is int pid &&
            !await _db.TaskComments.AnyAsync(x => x.Id == pid && x.TaskId == taskId))
            return BadRequest("The comment you are replying to was not found on this task.");

        var author = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == dto.AuthorId);

        var c = new TaskComment
        {
            TaskId = taskId,
            AuthorId = dto.AuthorId,
            AuthorName = author?.Name ?? $"#{dto.AuthorId}",
            Body = dto.Body,
            ParentId = dto.ParentId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.TaskComments.Add(c);
        await _db.SaveChangesAsync();
        return Ok(c.ToDto());
    }

    /// <summary>Delete a comment (UI restricts this to the author / a manager).</summary>
    [HttpDelete("api/comments/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.TaskComments.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        _db.TaskComments.Remove(c);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
