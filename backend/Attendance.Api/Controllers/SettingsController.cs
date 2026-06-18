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
    private const double MaxThreshold = 0.7;

    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

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
