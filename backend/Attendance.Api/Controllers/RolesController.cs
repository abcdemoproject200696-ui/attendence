using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;
    public RolesController(AppDbContext db, PermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetAll()
    {
        var list = await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        return Ok(list.Select(r => r.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleDto>> Get(int id)
    {
        var r = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? NotFound() : Ok(r.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(RoleInputDto dto)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Role name is required.");
        if (await _db.Roles.AnyAsync(r => r.Name == name))
            return BadRequest($"Role '{name}' already exists.");

        var role = new Role { Name = name, IsActive = dto.IsActive ?? true };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleDto>> Update(int id, RoleInputDto dto)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.Id == id);
        if (role is null) return NotFound();

        // Name optional on update: provided -> rename; omitted/blank -> keep existing.
        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var name = dto.Name.Trim();
            if (await _db.Roles.AnyAsync(r => r.Name == name && r.Id != id))
                return BadRequest($"Role '{name}' already exists.");
            role.Name = name;
        }

        if (dto.IsActive.HasValue)
            role.IsActive = dto.IsActive.Value;

        await _db.SaveChangesAsync();
        return Ok(role.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.Id == id);
        if (role is null) return NotFound();

        var inUse = await _db.Employees.CountAsync(e => e.RoleId == id);
        if (inUse > 0)
            return BadRequest($"Role is in use by {inUse} employee{(inUse == 1 ? "" : "s")}.");

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Page permissions ----------

    /// <summary>Page ids the role can access (Admin -> all page ids).</summary>
    [HttpGet("{roleId:int}/permissions")]
    public async Task<ActionResult<RolePermissionsDto>> GetPermissions(int roleId)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId)) return NotFound();
        var pageIds = await _permissions.GetAllowedPageIdsAsync(roleId);
        return Ok(new RolePermissionsDto(roleId, pageIds));
    }

    /// <summary>Replace the role's page permissions with the given page ids.</summary>
    [HttpPut("{roleId:int}/permissions")]
    public async Task<ActionResult<RolePermissionsDto>> UpdatePermissions(int roleId, RolePermissionsInputDto dto)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == roleId)) return NotFound();

        var requested = (dto.PageIds ?? new List<int>()).Distinct().ToList();
        var validPageIds = await _db.Pages.Select(p => p.Id).ToListAsync();
        var invalid = requested.Where(id => !validPageIds.Contains(id)).ToList();
        if (invalid.Count > 0)
            return BadRequest($"Unknown page id(s): {string.Join(", ", invalid)}.");

        var existing = await _db.RolePagePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.RolePagePermissions.RemoveRange(existing);
        foreach (var pageId in requested)
            _db.RolePagePermissions.Add(new RolePagePermission { RoleId = roleId, PageId = pageId });
        await _db.SaveChangesAsync();

        // Return resolved view (Admin still resolves to all).
        var pageIds = await _permissions.GetAllowedPageIdsAsync(roleId);
        return Ok(new RolePermissionsDto(roleId, pageIds));
    }
}
