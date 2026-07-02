using System.Text.RegularExpressions;
using Attendance.Api.Dtos;
using Attendance.Api.Services;
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
    private readonly EmailSender _email;
    public EmployeesController(AppDbContext db, EmailSender email)
    {
        _db = db;
        _email = email;
    }

    // Basic email-format check (server-side; the app validates too).
    private static readonly Regex EmailRx =
        new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmployeeDto>>> GetAll()
    {
        // Inactive employees are hidden unless the admin enabled "Show inactive employees".
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync();
        var showInactive = settings?.ShowInactiveEmployees ?? false;
        var q = _db.Employees.AsNoTracking().Include(e => e.Role).AsQueryable();
        if (!showInactive) q = q.Where(e => e.IsActive);
        var list = await q.OrderBy(e => e.Code).ToListAsync();
        return Ok(list.Select(e => e.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDto>> Get(int id)
    {
        var e = await _db.Employees.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? NotFound() : Ok(e.ToDto());
    }

    // Step 1 of OTP signup: email the applicant a 6-digit code and store it.
    // Only used when the admin enabled "Signup email OTP verification" in Settings.
    [HttpPost("signup/send-otp")]
    public async Task<IActionResult> SendSignupOtp(SendOtpRequestDto dto)
    {
        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!EmailRx.IsMatch(email)) return BadRequest("Please enter a valid email address.");

        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is null || !settings.SignupOtpEmail)
            return BadRequest("Email verification is turned off.");
        if (!_email.Enabled)
            return StatusCode(503, "Email is not configured on the server. Please contact the admin.");

        // 6-digit code; one row per email (replace any previous code).
        var code = Random.Shared.Next(100000, 1000000).ToString();
        var existing = await _db.SignupOtps.Where(o => o.Email == email).ToListAsync();
        _db.SignupOtps.RemoveRange(existing);
        _db.SignupOtps.Add(new SignupOtp { Email = email, Code = code, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var sent = await _email.SendSignupOtpAsync(email, code);
        if (!sent) return StatusCode(502, "Could not send the verification email. Please try again.");
        return Ok(new { sent = true });
    }

    // Public self-registration. ALWAYS created inactive (forced server-side so the
    // client can't bypass it) — an admin reviews the details and activates the
    // account; only then can the person log in (login + the active-account gate
    // both reject inactive users).
    [HttpPost("signup")]
    public async Task<ActionResult<EmployeeDto>> Signup(EmployeeInputDto dto)
    {
        dto.IsActive = false;

        // When OTP verification is on, require a matching, non-expired code for this email.
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync();
        if (settings is not null && settings.SignupOtpEmail)
        {
            var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!EmailRx.IsMatch(email)) return BadRequest("Please enter a valid email address.");
            if (string.IsNullOrWhiteSpace(dto.Otp)) return BadRequest("Please enter the verification code sent to your email.");

            var row = await _db.SignupOtps.FirstOrDefaultAsync(o => o.Email == email);
            if (row is null || row.Code != dto.Otp.Trim())
                return BadRequest("The verification code is incorrect. Please check and try again.");
            if (row.CreatedAt < DateTime.UtcNow.AddMinutes(-10))
            {
                _db.SignupOtps.Remove(row);
                await _db.SaveChangesAsync();
                return BadRequest("The verification code has expired. Please request a new one.");
            }
            // Verified — consume the code so it can't be reused.
            _db.SignupOtps.Remove(row);
            await _db.SaveChangesAsync();
        }

        return await Create(dto);
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

        var (first, last) = SplitNames(dto);
        if (string.IsNullOrWhiteSpace(first)) return BadRequest("Name is required.");

        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync();

        string code;
        if (!string.IsNullOrWhiteSpace(dto.Code))
        {
            // Manual code (admin typed it) — must be unique.
            code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Employees.AnyAsync(e => e.Code.ToUpper() == code))
                return BadRequest($"Employee code '{code}' already exists.");
        }
        else if (settings?.ManualEmpCode == true)
        {
            // Manual mode is on but no code was supplied.
            return BadRequest("Employee code is required (manual code is enabled in Settings).");
        }
        else
        {
            code = await GenerateNextCodeAsync(settings?.EmpCodeStart ?? 1);
        }

        var e = new Employee
        {
            Code = code,
            FirstName = first,
            LastName = last,
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
            Designation = dto.Designation,
            Department = dto.Department,
            DateOfJoining = dto.DateOfJoining,
            Aadhaar = dto.Aadhaar,
            Pan = dto.Pan,
            UanPf = dto.UanPf,
            BankAccount = dto.BankAccount,
            Ifsc = dto.Ifsc,
            BankName = dto.BankName,
            EmergencyName = dto.EmergencyName,
            EmergencyPhone = dto.EmergencyPhone,
            CurrentAddress = dto.CurrentAddress,
            PermanentAddress = dto.PermanentAddress,
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

        // The employee code is NEVER changed on edit (kept as-is).
        if (!await _db.Shifts.AnyAsync(s => s.Id == dto.ShiftId))
            return BadRequest($"Shift {dto.ShiftId} does not exist.");
        if (!await _db.Roles.AnyAsync(r => r.Id == dto.RoleId))
            return BadRequest($"Role {dto.RoleId} does not exist.");

        var faceError = ValidateFaceDescriptors(dto.FaceDescriptors);
        if (faceError is not null) return BadRequest(faceError);

        var (first, last) = SplitNames(dto);
        if (string.IsNullOrWhiteSpace(first)) return BadRequest("Name is required.");
        e.FirstName = first;
        e.LastName = last;
        e.RoleId = dto.RoleId;
        e.Email = dto.Email;
        e.Phone = dto.Phone;
        e.ShiftId = dto.ShiftId;
        e.MonthlySalary = dto.MonthlySalary;
        e.IsActive = dto.IsActive;
        e.Designation = dto.Designation;
        e.Department = dto.Department;
        e.DateOfJoining = dto.DateOfJoining;
        e.Aadhaar = dto.Aadhaar;
        e.Pan = dto.Pan;
        e.UanPf = dto.UanPf;
        e.BankAccount = dto.BankAccount;
        e.Ifsc = dto.Ifsc;
        e.BankName = dto.BankName;
        e.EmergencyName = dto.EmergencyName;
        e.EmergencyPhone = dto.EmergencyPhone;
        e.CurrentAddress = dto.CurrentAddress;
        e.PermanentAddress = dto.PermanentAddress;
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

    // Employees are NEVER hard-deleted (data retention) — this DEACTIVATES them
    // (IsActive = false). Their attendance/leave history is kept. A deactivated
    // employee can't log in, gets no notifications, and is hidden from the list
    // unless the admin enables "Show inactive employees".
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        // Admin accounts are protected: nobody can deactivate an admin.
        if (e.RoleId == 1)
            return StatusCode(403, "Admin accounts cannot be deactivated.");

        e.IsActive = false;
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

    /// <summary>Resolve First/Last from the DTO. Uses FirstName/LastName when sent;
    /// otherwise splits the legacy single Name ("Rohan Gupta" → "Rohan","Gupta").</summary>
    private static (string first, string? last) SplitNames(EmployeeInputDto dto)
    {
        var first = dto.FirstName?.Trim();
        var last = dto.LastName?.Trim();
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last)
            && !string.IsNullOrWhiteSpace(dto.Name))
        {
            var parts = dto.Name.Trim().Split(' ', 2);
            first = parts[0];
            last = parts.Length > 1 ? parts[1] : null;
        }
        return (first ?? string.Empty, string.IsNullOrWhiteSpace(last) ? null : last);
    }

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

    /// <summary>Next "EMP00X": max numeric suffix of existing EMP-codes + 1, but never
    /// below <paramref name="start"/> (the admin-set starting number). Zero-padded to 3.</summary>
    private async Task<string> GenerateNextCodeAsync(int start = 1)
    {
        var codes = await _db.Employees.Select(e => e.Code).ToListAsync();
        var max = 0;
        foreach (var c in codes)
        {
            var m = Regex.Match(c ?? string.Empty, @"^EMP(\d+)$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max)
                max = n;
        }
        var next = Math.Max(max + 1, start < 1 ? 1 : start);
        return $"EMP{next:D3}";
    }
}
