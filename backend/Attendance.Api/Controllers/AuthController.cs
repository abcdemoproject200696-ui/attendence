using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;

    public AuthController(AppDbContext db, PermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    /// <summary>Basic login: validate code + password, return profile and allowed page keys.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<LoginResultDto>> Login(LoginRequestDto dto)
    {
        // Code is matched case-INSENSITIVELY (emp001 == EMP001). Password stays EXACT.
        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Code.ToUpper() == code && e.IsActive);

        if (employee is null || !PasswordHasher.Verify(dto.Password ?? string.Empty, employee.PasswordHash))
            return Unauthorized("Invalid code or password.");

        var allowedPages = await _permissions.GetAllowedPageKeysAsync(employee.RoleId);

        return Ok(new LoginResultDto(
            employee.Id, employee.Code, employee.Name, employee.RoleId,
            employee.Role?.Name ?? string.Empty, allowedPages));
    }
}
