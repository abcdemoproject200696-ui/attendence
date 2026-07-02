using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private const double MinThreshold = 0.3;
    // Raised from 0.7 to allow web (face-api crop) matching of mobile (ML Kit crop)
    // enrolled faces, whose cross-detector distance runs a bit higher. The match
    // margin (winner must be clearly closer than runner-up) still guards false matches.
    private const double MaxThreshold = 1.2;

    private readonly AppDbContext _db;
    private readonly Attendance.Api.Services.EmailSender _email;
    public SettingsController(AppDbContext db, Attendance.Api.Services.EmailSender email)
    {
        _db = db;
        _email = email;
    }

    /// <summary>Diagnostic: try sending a test email and return the REAL SMTP error (if any)
    /// plus the (non-secret) config being used — so the admin can fix it without server logs.
    /// Example: GET /api/settings/email-test?to=me@example.com</summary>
    [HttpGet("email-test")]
    public async Task<IActionResult> EmailTest([FromQuery] string to)
    {
        var error = await _email.SendReturningErrorAsync(
            to, "Test email — Tech Anusiya Attendance",
            "<p>This is a test email. If you can read this, SMTP is working. ✅</p>");
        return Ok(new
        {
            ok = error is null,
            error,
            usingHost = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "(default) smtp.gmail.com",
            usingPort = Environment.GetEnvironmentVariable("SMTP_PORT") ?? "(default) 587",
            usingUser = Environment.GetEnvironmentVariable("SMTP_USER"),
            usingFrom = Environment.GetEnvironmentVariable("SMTP_FROM"),
        });
    }

    // -------------------- GET --------------------
    [HttpGet]
    public async Task<ActionResult<AppSettingDto>> Get()
    {
        var settings = await GetOrCreateAsync();
        return Ok(settings.ToDto());
    }

    // -------------------- PUT --------------------
    [HttpPut]
    public async Task<ActionResult<AppSettingDto>> Update(AppSettingUpdateDto dto)
    {
        if (dto.FaceMatchThreshold is { } t && (t < MinThreshold || t > MaxThreshold))
            return BadRequest($"faceMatchThreshold must be between {MinThreshold} and {MaxThreshold}.");

        var settings = await GetOrCreateAsync();
        if (dto.FaceMatchThreshold.HasValue) settings.FaceMatchThreshold = dto.FaceMatchThreshold.Value;
        if (dto.RequireLiveness.HasValue) settings.RequireLiveness = dto.RequireLiveness.Value;
        if (dto.VoiceEnabled.HasValue) settings.VoiceEnabled = dto.VoiceEnabled.Value;
        if (dto.OvertimePayable.HasValue) settings.OvertimePayable = dto.OvertimePayable.Value;
        if (dto.HrCanEditAttendance.HasValue) settings.HrCanEditAttendance = dto.HrCanEditAttendance.Value;
        if (dto.TaskAssignEmail.HasValue) settings.TaskAssignEmail = dto.TaskAssignEmail.Value;
        if (dto.SignupOtpEmail.HasValue) settings.SignupOtpEmail = dto.SignupOtpEmail.Value;

        await _db.SaveChangesAsync();
        return Ok(settings.ToDto());
    }

    /// <summary>Return the single settings row, creating the default if none exists.</summary>
    private async Task<AppSetting> GetOrCreateAsync()
    {
        var settings = await _db.Settings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new AppSetting { FaceMatchThreshold = 0.5, RequireLiveness = false };
            _db.Settings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }
}
