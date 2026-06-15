using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/leaves")]
public class LeavesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttendanceService _svc;
    public LeavesController(AppDbContext db, AttendanceService svc)
    {
        _db = db;
        _svc = svc;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LeaveDto>>> GetAll(
        [FromQuery] int? employeeId, [FromQuery] string? month)
    {
        var q = _db.Leaves.AsNoTracking().Include(l => l.Employee).AsQueryable();
        if (employeeId.HasValue) q = q.Where(l => l.EmployeeId == employeeId.Value);

        if (!string.IsNullOrWhiteSpace(month) &&
            DateTime.TryParse(month + "-01", out var first))
        {
            var last = first.AddMonths(1).AddDays(-1);
            // overlap with the month range
            q = q.Where(l => l.FromDate <= last && l.ToDate >= first);
        }

        var list = await q.OrderByDescending(l => l.FromDate).ToListAsync();
        return Ok(list.Select(l => l.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeaveDto>> Get(int id)
    {
        var l = await _db.Leaves.AsNoTracking().Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == id);
        return l is null ? NotFound() : Ok(l.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<LeaveDto>> Create(LeaveInputDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == dto.EmployeeId))
            return BadRequest($"Employee {dto.EmployeeId} does not exist.");
        if (dto.ToDate.Date < dto.FromDate.Date)
            return BadRequest("toDate cannot be before fromDate.");

        var l = new LeaveRequest
        {
            EmployeeId = dto.EmployeeId,
            FromDate = dto.FromDate.Date,
            ToDate = dto.ToDate.Date,
            Type = dto.Type,
            IsPaid = dto.IsPaid,
            Reason = dto.Reason,
            Status = LeaveStatus.Pending
        };
        _db.Leaves.Add(l);
        await _db.SaveChangesAsync();
        await _db.Entry(l).Reference(x => x.Employee).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = l.Id }, l.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<LeaveDto>> Update(int id, LeaveUpdateDto dto)
    {
        var l = await _db.Leaves.Include(x => x.Employee).FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return NotFound();

        var wasApproved = l.Status == LeaveStatus.Approved;
        var oldFrom = l.FromDate;
        var oldTo = l.ToDate;

        if (dto.Status.HasValue) l.Status = dto.Status.Value;
        if (dto.FromDate.HasValue) l.FromDate = dto.FromDate.Value.Date;
        if (dto.ToDate.HasValue) l.ToDate = dto.ToDate.Value.Date;
        if (dto.Type.HasValue) l.Type = dto.Type.Value;
        if (dto.IsPaid.HasValue) l.IsPaid = dto.IsPaid.Value;
        if (dto.Reason != null) l.Reason = dto.Reason;

        if (l.ToDate < l.FromDate) return BadRequest("toDate cannot be before fromDate.");

        await _db.SaveChangesAsync();

        // If approval state or dates changed, recompute affected days so status reflects the leave.
        var nowApproved = l.Status == LeaveStatus.Approved;
        if (wasApproved || nowApproved)
        {
            var rangeStart = Min(oldFrom, l.FromDate);
            var rangeEnd = Max(oldTo, l.ToDate);
            for (var d = rangeStart.Date; d <= rangeEnd.Date; d = d.AddDays(1))
            {
                try { await _svc.RecomputeDayAsync(l.EmployeeId, d); }
                catch (InvalidOperationException) { /* skip if employee/shift missing */ }
            }
        }

        return Ok(l.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var l = await _db.Leaves.FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return NotFound();
        var wasApproved = l.Status == LeaveStatus.Approved;
        var from = l.FromDate;
        var to = l.ToDate;
        var empId = l.EmployeeId;

        _db.Leaves.Remove(l);
        await _db.SaveChangesAsync();

        if (wasApproved)
        {
            for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                try { await _svc.RecomputeDayAsync(empId, d); }
                catch (InvalidOperationException) { }
            }
        }
        return NoContent();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
