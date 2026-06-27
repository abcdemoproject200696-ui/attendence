using System.Text.RegularExpressions;
using Attendance.Api.Dtos;
using Attendance.Domain;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
    {
        var list = await _db.Employees.AsNoTracking().Include(e => e.Role).OrderBy(e => e.Code).ToListAsync();
        return Ok(list.Select(e => e.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDto>> Get(int id)
    {
        var e = await _db.Employees.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? NotFound() : Ok(e.ToDto());
    }

    // Public self-registration. ALWAYS created inactive (forced server-side so the
    // client can't bypass it) — an admin reviews the details and activates the
    // account; only then can the person log in (login + the active-account gate
    // both reject inactive users).
    [HttpPost("signup")]
    public Task<ActionResult<EmployeeDto>> Signup(EmployeeInputDto dto)
    {
        dto.IsActive = false;
        return Create(dto);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create(EmployeeInputDto dto)
    {
        if (!await _db.Shifts.AnyAsync(s => s.Id == dto.ShiftId))
            return BadRequest($"Shift {dto.ShiftId} does not exist.");
        if (!await _db.Roles.AnyAsync(r => r.Id == dto.RoleId))
            return BadRequest($"Role {dto.RoleId} does not exist.");

        var faceError = ValidateFaceDescriptors(dto.FaceDescriptors);
        if (faceError is not null) return BadRequest(faceError);

        string code;
        if (string.IsNullOrWhiteSpace(dto.Code))
        {
            code = await GenerateNextCodeAsync();
        }
        else
        {
            code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Employees.AnyAsync(e => e.Code.ToUpper() == code))
                return BadRequest($"Employee code '{code}' already exists.");
        }

        var e = new Employee
        {
            Code = code,
            Name = dto.Name,
            RoleId = dto.RoleId,
            Email = dto.Email,
            Phone = dto.Phone,
            ShiftId = dto.ShiftId,
            MonthlySalary = dto.MonthlySalary,
            IsActive = dto.IsActive,
            PhotoUrl = dto.PhotoUrl,
            Gender = dto.Gender,
            BloodGroup = dto.BloodGroup,
            Dob = dto.Dob,
            FaceDescriptors = dto.FaceDescriptors is { Count: > 0 } ? dto.FaceDescriptors : null,
            PasswordHash = string.IsNullOrEmpty(dto.Password) ? null : PasswordHasher.Hash(dto.Password),
            CreatedAt = DateTime.UtcNow
        };
        _db.Employees.Add(e);
        await _db.SaveChangesAsync();
        await _db.Entry(e).Reference(x => x.Role).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = e.Id }, e.ToDto());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EmployeeDto>> Update(int id, EmployeeInputDto dto)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        var newCode = string.IsNullOrWhiteSpace(dto.Code) ? e.Code : dto.Code.Trim().ToUpperInvariant();
        if (e.Code != newCode && await _db.Employees.AnyAsync(x => x.Code.ToUpper() == newCode && x.Id != id))
            return Conflict($"Employee code '{newCode}' already exists.");
        if (!await _db.Shifts.AnyAsync(s => s.Id == dto.ShiftId))
            return BadRequest($"Shift {dto.ShiftId} does not exist.");
        if (!await _db.Roles.AnyAsync(r => r.Id == dto.RoleId))
            return BadRequest($"Role {dto.RoleId} does not exist.");

        var faceError = ValidateFaceDescriptors(dto.FaceDescriptors);
        if (faceError is not null) return BadRequest(faceError);

        e.Code = newCode;
        e.Name = dto.Name;
        e.RoleId = dto.RoleId;
        e.Email = dto.Email;
        e.Phone = dto.Phone;
        e.ShiftId = dto.ShiftId;
        e.MonthlySalary = dto.MonthlySalary;
        e.IsActive = dto.IsActive;
        // Provided non-empty => set new photo. Omitted/empty => KEEP the existing
        // enrolment photo. Without this guard, editing anything else (e.g. salary)
        // sent no photoUrl and wiped the picture, forcing a needless re-enrollment.
        if (!string.IsNullOrEmpty(dto.PhotoUrl))
            e.PhotoUrl = dto.PhotoUrl;
        e.Gender = dto.Gender;
        e.BloodGroup = dto.BloodGroup;
        e.Dob = dto.Dob;
        // Provided => APPEND to existing enrolled faces (keep the most recent 10) so a
        // person can be enrolled on BOTH web and mobile and recognised on either platform
        // (each platform's scan matches its own enrolled set). Omitted/empty => keep existing.
        if (dto.FaceDescriptors is { Count: > 0 })
        {
            var combined = (e.FaceDescriptors ?? new List<List<double>>())
                .Concat(dto.FaceDescriptors).ToList();
            const int maxStored = 10;
            if (combined.Count > maxStored)
                combined = combined.Skip(combined.Count - maxStored).ToList();
            e.FaceDescriptors = combined;
        }
        // Provided non-empty => set/reset password. Empty/null => keep existing.
        if (!string.IsNullOrEmpty(dto.Password))
            e.PasswordHash = PasswordHasher.Hash(dto.Password);

        await _db.SaveChangesAsync();
        await _db.Entry(e).Reference(x => x.Role).LoadAsync();
        return Ok(e.ToDto());
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        // Admin accounts are protected: NOBODY (not even another admin or HR) can
        // delete them. Enforced server-side so no client can bypass it.
        if (e.RoleId == 1)
            return StatusCode(403, "Admin accounts cannot be deleted.");

        // Full cascade: remove ALL of this employee's related records first, then the
        // employee (face descriptors live on the employee row, so they go with it).
        _db.Punches.RemoveRange(_db.Punches.Where(p => p.EmployeeId == id));
        _db.Days.RemoveRange(_db.Days.Where(d => d.EmployeeId == id));
        _db.Leaves.RemoveRange(_db.Leaves.Where(l => l.EmployeeId == id));
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Validate enrolled face descriptors: at most 5; each a sane vector length; and
    /// all the SAME length within a request. We don't hardcode 128 because different
    /// clients use different embedders (Ionic face-api = 128-d, Flutter MobileFaceNet = 192-d).
    /// Returns an error message (=> 400) or null when valid (null/empty = no faces).
    /// </summary>
    private const int MinFaceDescriptorLength = 64;
    private const int MaxFaceDescriptorLength = 1024;
    private const int MaxFaceDescriptors = 5;

    private static string? ValidateFaceDescriptors(List<List<double>>? descriptors)
    {
        if (descriptors is null || descriptors.Count == 0) return null;
        if (descriptors.Count > MaxFaceDescriptors)
            return $"At most {MaxFaceDescriptors} face descriptors allowed (got {descriptors.Count}).";
        int? len = null;
        for (var i = 0; i < descriptors.Count; i++)
        {
            var d = descriptors[i];
            if (d is null || d.Count < MinFaceDescriptorLength || d.Count > MaxFaceDescriptorLength)
                return $"Face descriptor #{i + 1} has an invalid size ({d?.Count ?? 0}).";
            len ??= d.Count;
            if (d.Count != len)
                return "All face descriptors must have the same length.";
        }
        return null;
    }

    /// <summary>Next "EMP00X": max numeric suffix of existing EMP-codes + 1, zero-padded to 3.</summary>
    private async Task<string> GenerateNextCodeAsync()
    {
        var codes = await _db.Employees.Select(e => e.Code).ToListAsync();
        var max = 0;
        foreach (var c in codes)
        {
            var m = Regex.Match(c ?? string.Empty, @"^EMP(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max)
                max = n;
        }
        return $"EMP{(max + 1):D3}";
    }
}
