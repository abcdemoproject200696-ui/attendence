using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PushSender _push;
    public DevicesController(AppDbContext db, PushSender push)
    {
        _db = db;
        _push = push;
    }

    /// <summary>Diagnostics (no secrets): is FCM configured on the server, and how many
    /// device tokens are registered (optionally for one employee)?</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] int? employeeId)
    {
        var total = await _db.DeviceTokens.CountAsync();
        int? forEmp = employeeId is int e ? await _db.DeviceTokens.CountAsync(d => d.EmployeeId == e) : null;
        return Ok(new { pushEnabled = _push.Enabled, totalTokens = total, employeeTokens = forEmp });
    }

    /// <summary>Register (or refresh) this device's FCM token for an employee, so the
    /// server can push task-assigned notifications to their phone. Idempotent: the
    /// same token just updates its owner + timestamp.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register(DeviceTokenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || dto.EmployeeId <= 0)
            return BadRequest("employeeId and token are required.");

        var row = await _db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == dto.Token);
        if (row is null)
        {
            row = new DeviceToken { Token = dto.Token.Trim() };
            _db.DeviceTokens.Add(row);
        }
        row.EmployeeId = dto.EmployeeId;
        row.Platform = dto.Platform;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { registered = true });
    }
}
