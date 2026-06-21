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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetAll([FromQuery] int? assigneeId)
    {
        var q = _db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.AssignedBy)
            .AsQueryable();
        if (assigneeId is int aid)
            q = q.Where(t => t.AssigneeId == aid);
        var list = await q.OrderByDescending(t => t.Id).ToListAsync();
        return Ok(list.Select(t => t.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TaskDto>> Get(int id)
    {
        var t = await _db.Tasks.AsNoTracking()
            .Include(x => x.Assignee)
            .Include(x => x.AssignedBy)
            .FirstOrDefaultAsync(x => x.Id == id);
        return t is null ? NotFound() : Ok(t.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(TaskInputDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == dto.AssigneeId))
            return BadRequest($"Employee {dto.AssigneeId} does not exist.");

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
            CreatedAt = DateTime.UtcNow
        };
        _db.Tasks.Add(t);
        await _db.SaveChangesAsync();
        await _db.Entry(t).Reference(x => x.Assignee).LoadAsync();
        await _db.Entry(t).Reference(x => x.AssignedBy).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = t.Id }, t.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TaskDto>> Update(int id, TaskInputDto dto)
    {
        var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        if (!await _db.Employees.AnyAsync(e => e.Id == dto.AssigneeId))
            return BadRequest($"Employee {dto.AssigneeId} does not exist.");

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

        await _db.SaveChangesAsync();
        await _db.Entry(t).Reference(x => x.Assignee).LoadAsync();
        await _db.Entry(t).Reference(x => x.AssignedBy).LoadAsync();
        return Ok(t.ToDto());
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
        return Ok(t.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        _db.Tasks.Remove(t);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
