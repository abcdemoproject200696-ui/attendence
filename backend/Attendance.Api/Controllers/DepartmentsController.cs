using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

public record DepartmentDto(int Id, string Name, bool IsActive);
public class DepartmentInputDto { public string? Name { get; set; } public bool IsActive { get; set; } = true; }

[ApiController]
[Route("api/departments")]
public class DepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public DepartmentsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DepartmentDto>>> GetAll()
    {
        var list = await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
        return Ok(list.Select(d => new DepartmentDto(d.Id, d.Name, d.IsActive)));
    }

    [HttpPost]
    public async Task<ActionResult<DepartmentDto>> Create(DepartmentInputDto dto)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Department name is required.");
        if (await _db.Departments.AnyAsync(d => d.Name.ToLower() == name.ToLower()))
            return BadRequest($"Department '{name}' already exists.");
        var d = new Department { Name = name, IsActive = dto.IsActive };
        _db.Departments.Add(d);
        await _db.SaveChangesAsync();
        return Ok(new DepartmentDto(d.Id, d.Name, d.IsActive));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DepartmentDto>> Update(int id, DepartmentInputDto dto)
    {
        var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        var name = (dto.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("Department name is required.");
        if (await _db.Departments.AnyAsync(x => x.Name.ToLower() == name.ToLower() && x.Id != id))
            return BadRequest($"Department '{name}' already exists.");
        d.Name = name;
        d.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new DepartmentDto(d.Id, d.Name, d.IsActive));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        _db.Departments.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
