using Attendance.Api.Dtos;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/pages")]
public class PagesController : ControllerBase
{
    private readonly AppDbContext _db;
    public PagesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PageDto>>> GetAll()
    {
        var list = await _db.Pages.AsNoTracking().OrderBy(p => p.MenuOrder).ToListAsync();
        return Ok(list.Select(p => p.ToDto()));
    }
}
