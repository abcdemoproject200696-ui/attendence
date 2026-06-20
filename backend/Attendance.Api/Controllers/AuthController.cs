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
    private readonly OtpService _otp;

    public AuthController(AppDbContext db, PermissionService permissions, OtpService otp)
    {
        _db = db;
        _permissions = permissions;
        _otp = otp;
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
            employee.Role?.Name ?? string.Empty, allowedPages, employee.PhotoUrl));
    }

    /// <summary>Forgot password step 1: if an active employee has this code, return a (demo) OTP.</summary>
    [HttpPost("forgot-otp")]
    public async Task<ActionResult<OtpResponseDto>> ForgotOtp(ForgotOtpRequestDto dto)
    {
        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        var exists = await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.Code.ToUpper() == code && e.IsActive);
        if (!exists) return NotFound("This employee code is not registered.");

        var otp = _otp.Generate(code);
        return Ok(new OtpResponseDto(otp));
    }

    /// <summary>Forgot password step 2: verify the OTP and set a new password for the employee.</summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto dto)
    {
        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!_otp.Verify(code, dto.Otp ?? string.Empty)) return BadRequest("Invalid OTP.");
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 4)
            return BadRequest("Password must be at least 4 characters.");

        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Code.ToUpper() == code && e.IsActive);
        if (employee is null) return NotFound("This employee code is not registered.");

        employee.PasswordHash = PasswordHasher.Hash(dto.Password);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
