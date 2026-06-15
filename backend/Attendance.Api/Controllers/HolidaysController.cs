using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/holidays")]
public class HolidaysController : ControllerBase
{
    private readonly AppDbContext _db;
    public HolidaysController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<HolidayDto>>> GetAll([FromQuery] int? year)
    {
        var q = _db.Holidays.AsNoTracking().AsQueryable();
        if (year.HasValue) q = q.Where(h => h.Date.Year == year.Value);
        var list = await q.OrderBy(h => h.Date).ToListAsync();
        return Ok(list.Select(h => h.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HolidayDto>> Get(int id)
    {
        var h = await _db.Holidays.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return h is null ? NotFound() : Ok(h.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<HolidayDto>> Create(HolidayInputDto dto)
    {
        var h = new Holiday { Date = dto.Date.Date, Name = dto.Name, IsPaid = dto.IsPaid };
        _db.Holidays.Add(h);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = h.Id }, h.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<HolidayDto>> Update(int id, HolidayInputDto dto)
    {
        var h = await _db.Holidays.FirstOrDefaultAsync(x => x.Id == id);
        if (h is null) return NotFound();
        h.Date = dto.Date.Date;
        h.Name = dto.Name;
        h.IsPaid = dto.IsPaid;
        await _db.SaveChangesAsync();
        return Ok(h.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var h = await _db.Holidays.FirstOrDefaultAsync(x => x.Id == id);
        if (h is null) return NotFound();
        _db.Holidays.Remove(h);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
