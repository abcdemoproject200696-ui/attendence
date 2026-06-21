using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    public TasksController(AppDbContext db) => _db = db;

    private static readonly string[] ValidStatuses = { "ToDo", "InProgress", "Review", "Done" };
    private static readonly string[] ValidPriorities = { "Low", "Medium", "High", "Urgent" };

    /// <summary>AttachmentCount per task id, for the given task ids.</summary>
    private async Task<Dictionary<int, int>> AttachmentCountsAsync(IEnumerable<int> taskIds)
    {
        var ids = taskIds.ToList();
        if (ids.Count == 0) return new();
        return await _db.TaskAttachments.AsNoTracking()
            .Where(a => ids.Contains(a.TaskId))
            .GroupBy(a => a.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetAll(
        [FromQuery] int? assigneeId, [FromQuery] int? projectId)
    {
        var q = _db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.AssignedBy)
            .Include(t => t.Project)
            .AsQueryable();
        if (assigneeId is int aid)
            q = q.Where(t => t.AssigneeId == aid);
        if (projectId is int pid)
            q = q.Where(t => t.ProjectId == pid);
        var list = await q.OrderByDescending(t => t.Id).ToListAsync();
        var counts = await AttachmentCountsAsync(list.Select(t => t.Id));
        return Ok(list.Select(t => t.ToDto(counts.GetValueOrDefault(t.Id))));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskDto>> Get(int id)
    {
        var t = await _db.Tasks.AsNoTracking()
            .Include(x => x.Assignee)
            .Include(x => x.AssignedBy)
            .Include(x => x.Project)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        var count = await _db.TaskAttachments.CountAsync(a => a.TaskId == id);
        return Ok(t.ToDto(count));
    }

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(TaskInputDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == dto.AssigneeId))
            return BadRequest($"Employee {dto.AssigneeId} does not exist.");

        if (dto.ProjectId is int pid && !await _db.Projects.AnyAsync(p => p.Id == pid))
            return BadRequest($"Project {pid} does not exist.");

        var status = string.IsNullOrWhiteSpace(dto.Status) ? "ToDo" : dto.Status;
        if (!ValidStatuses.Contains(status))
            return BadRequest($"Invalid status '{status}'. Allowed: {string.Join(", ", ValidStatuses)}.");
        var priority = string.IsNullOrWhiteSpace(dto.Priority) ? "Medium" : dto.Priority;
        if (!ValidPriorities.Contains(priority))
            return BadRequest($"Invalid priority '{priority}'. Allowed: {string.Join(", ", ValidPriorities)}.");

        var t = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            AssigneeId = dto.AssigneeId,
            AssignedById = dto.AssignedById ?? 0,
            Status = status,
            Priority = priority,
            DueDate = dto.DueDate,
            ProjectId = dto.ProjectId,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            CreatedAt = DateTime.UtcNow
        };
        _db.Tasks.Add(t);
        await _db.SaveChangesAsync();
        await _db.Entry(t).Reference(x => x.Assignee).LoadAsync();
        await _db.Entry(t).Reference(x => x.AssignedBy).LoadAsync();
        await _db.Entry(t).Reference(x => x.Project).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = t.Id }, t.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TaskDto>> Update(int id, TaskInputDto dto)
    {
        var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        if (!await _db.Employees.AnyAsync(e => e.Id == dto.AssigneeId))
            return BadRequest($"Employee {dto.AssigneeId} does not exist.");

        if (dto.ProjectId is int pid && !await _db.Projects.AnyAsync(p => p.Id == pid))
            return BadRequest($"Project {pid} does not exist.");

        var status = string.IsNullOrWhiteSpace(dto.Status) ? t.Status : dto.Status;
        if (!ValidStatuses.Contains(status))
            return BadRequest($"Invalid status '{status}'. Allowed: {string.Join(", ", ValidStatuses)}.");
        var priority = string.IsNullOrWhiteSpace(dto.Priority) ? t.Priority : dto.Priority;
        if (!ValidPriorities.Contains(priority))
            return BadRequest($"Invalid priority '{priority}'. Allowed: {string.Join(", ", ValidPriorities)}.");

        t.Title = dto.Title;
        t.Description = dto.Description;
        t.AssigneeId = dto.AssigneeId;
        t.Status = status;
        t.Priority = priority;
        t.DueDate = dto.DueDate;
        t.ProjectId = dto.ProjectId;
        t.StartTime = dto.StartTime;
        t.EndTime = dto.EndTime;

        await _db.SaveChangesAsync();
        await _db.Entry(t).Reference(x => x.Assignee).LoadAsync();
        await _db.Entry(t).Reference(x => x.AssignedBy).LoadAsync();
        await _db.Entry(t).Reference(x => x.Project).LoadAsync();
        var count = await _db.TaskAttachments.CountAsync(a => a.TaskId == id);
        return Ok(t.ToDto(count));
    }

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<TaskDto>> UpdateStatus(int id, TaskStatusInputDto dto)
    {
        if (!ValidStatuses.Contains(dto.Status))
            return BadRequest($"Invalid status '{dto.Status}'. Allowed: {string.Join(", ", ValidStatuses)}.");

        var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        t.Status = dto.Status;
        await _db.SaveChangesAsync();
        await _db.Entry(t).Reference(x => x.Assignee).LoadAsync();
        await _db.Entry(t).Reference(x => x.AssignedBy).LoadAsync();
        await _db.Entry(t).Reference(x => x.Project).LoadAsync();
        var count = await _db.TaskAttachments.CountAsync(a => a.TaskId == id);
        return Ok(t.ToDto(count));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        // Remove this task's attachments first (explicit RemoveRange) so no orphan rows
        // remain on the raw-SQL/Postgres path — the EF cascade config also covers SQLite.
        _db.TaskAttachments.RemoveRange(_db.TaskAttachments.Where(a => a.TaskId == id));
        _db.Tasks.Remove(t);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
