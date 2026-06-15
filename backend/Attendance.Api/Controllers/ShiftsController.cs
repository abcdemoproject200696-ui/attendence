using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/shifts")]
public class ShiftsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ShiftsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> GetAll()
    {
        var list = await _db.Shifts.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        return Ok(list.Select(s => s.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShiftDto>> Get(int id)
    {
        var s = await _db.Shifts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return s is null ? NotFound() : Ok(s.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ShiftDto>> Create(ShiftInputDto dto)
    {
        var s = Apply(new Shift(), dto);
        _db.Shifts.Add(s);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = s.Id }, s.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ShiftDto>> Update(int id, ShiftInputDto dto)
    {
        var s = await _db.Shifts.FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        Apply(s, dto);
        await _db.SaveChangesAsync();
        return Ok(s.ToDto());
    }

    private static Shift Apply(Shift s, ShiftInputDto dto)
    {
        s.Name = dto.Name;
        s.ShiftStart = dto.ShiftStart;
        s.ShiftEnd = dto.ShiftEnd;
        s.RequiredMinutes = dto.RequiredMinutes;
        s.LunchStart = dto.LunchStart;
        s.LunchEnd = dto.LunchEnd;
        s.AutoDeductLunch = dto.AutoDeductLunch;
        s.LunchPaid = dto.LunchPaid;
        s.GraceMinutes = dto.GraceMinutes;
        s.HalfDayThresholdMinutes = dto.HalfDayThresholdMinutes;
        s.WeeklyOffDays = dto.WeeklyOffDays ?? new List<int>();
        return s;
    }
}
