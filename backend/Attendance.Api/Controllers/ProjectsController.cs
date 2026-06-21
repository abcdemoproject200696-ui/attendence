using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProjectsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetAll()
    {
        var projects = await _db.Projects.AsNoTracking()
            .Include(p => p.CreatedBy)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        // Per-project task count + distinct assignee count, computed from Tasks.
        var stats = await _db.Tasks.AsNoTracking()
            .Where(t => t.ProjectId != null)
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new
            {
                ProjectId = g.Key,
                TaskCount = g.Count(),
                EmployeeCount = g.Select(t => t.AssigneeId).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.ProjectId, x => x);

        return Ok(projects.Select(p =>
        {
            var s = stats.GetValueOrDefault(p.Id);
            return p.ToDto(s?.TaskCount ?? 0, s?.EmployeeCount ?? 0);
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectDetailDto>> Get(int id)
    {
        var p = await _db.Projects.AsNoTracking()
            .Include(x => x.CreatedBy)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        var tasks = await _db.Tasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.AssignedBy)
            .Include(t => t.Project)
            .Where(t => t.ProjectId == id)
            .OrderByDescending(t => t.Id)
            .ToListAsync();

        var taskIds = tasks.Select(t => t.Id).ToList();
        var counts = taskIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.TaskAttachments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskId))
                .GroupBy(a => a.TaskId)
                .Select(g => new { TaskId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TaskId, x => x.Count);

        var taskDtos = tasks.Select(t => t.ToDto(counts.GetValueOrDefault(t.Id))).ToList();

        // Distinct employees involved (assignees across the project's tasks).
        var employees = tasks
            .Where(t => t.Assignee != null)
            .Select(t => t.Assignee!)
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .Select(e => new EmployeeBriefDto(e.Id, e.Name, e.Code))
            .OrderBy(e => e.Name)
            .ToList();

        return Ok(new ProjectDetailDto(
            p.Id, p.Name, p.Description, p.Status, p.CreatedById, p.CreatedBy?.Name,
            p.CreatedAt, taskDtos.Count, employees.Count, taskDtos, employees));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(ProjectInputDto dto)
    {
        var p = new Project
        {
            Name = dto.Name,
            Description = dto.Description,
            Status = NormalizeStatus(dto.Status),
            CreatedById = dto.CreatedById ?? 0,
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();
        await _db.Entry(p).Reference(x => x.CreatedBy).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p.ToDto(0, 0));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProjectDto>> Update(int id, ProjectInputDto dto)
    {
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        p.Name = dto.Name;
        p.Description = dto.Description;
        if (dto.Status is not null) p.Status = NormalizeStatus(dto.Status);
        if (dto.CreatedById is int cid) p.CreatedById = cid;

        await _db.SaveChangesAsync();
        await _db.Entry(p).Reference(x => x.CreatedBy).LoadAsync();

        var taskCount = await _db.Tasks.CountAsync(t => t.ProjectId == id);
        var empCount = await _db.Tasks.Where(t => t.ProjectId == id)
            .Select(t => t.AssigneeId).Distinct().CountAsync();
        return Ok(p.ToDto(taskCount, empCount));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        // Full cascade: delete this project's tasks AND those tasks' attachments first,
        // then the project (explicit RemoveRange for the raw-SQL/Postgres path safety).
        var taskIds = await _db.Tasks.Where(t => t.ProjectId == id).Select(t => t.Id).ToListAsync();
        if (taskIds.Count > 0)
        {
            _db.TaskAttachments.RemoveRange(_db.TaskAttachments.Where(a => taskIds.Contains(a.TaskId)));
            _db.Tasks.RemoveRange(_db.Tasks.Where(t => t.ProjectId == id));
        }
        _db.Projects.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Map any input to one of the 3 allowed project states; default "Active".
    private static string NormalizeStatus(string? s)
    {
        var v = (s ?? "").Trim();
        return v.Equals("Hold", System.StringComparison.OrdinalIgnoreCase) ? "Hold"
             : v.Equals("Inactive", System.StringComparison.OrdinalIgnoreCase) ? "Inactive"
             : "Active";
    }
}
