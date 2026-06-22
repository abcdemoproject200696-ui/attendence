using Attendance.Api.Dtos;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AttachmentsController(AppDbContext db) => _db = db;

    /// <summary>List a task's attachments (no data, so the response stays light).</summary>
    [HttpGet("api/tasks/{taskId:int}/attachments")]
    public async Task<ActionResult<IEnumerable<AttachmentDto>>> ListForTask(int taskId)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId))
            return NotFound($"Task {taskId} does not exist.");

        var list = await _db.TaskAttachments.AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.Id)
            .ToListAsync();
        return Ok(list.Select(a => a.ToDto()));
    }

    /// <summary>Upload an attachment to a task. Returns the metadata (no data).</summary>
    [HttpPost("api/tasks/{taskId:int}/attachments")]
    public async Task<ActionResult<AttachmentDto>> Upload(int taskId, AttachmentInputDto dto)
    {
        if (!await _db.Tasks.AnyAsync(t => t.Id == taskId))
            return BadRequest($"Task {taskId} does not exist.");

        // Cap size: attachments are stored as base64 in the free 1GB DB, so a few
        // big videos could fill it. ~35M base64 chars ≈ 25MB file.
        if ((dto.DataBase64?.Length ?? 0) > 35_000_000)
            return BadRequest("File is too large. Please keep attachments under ~25 MB.");

        var a = new TaskAttachment
        {
            TaskId = taskId,
            FileName = dto.FileName,
            MimeType = dto.MimeType,
            DataBase64 = dto.DataBase64,
            CreatedAt = DateTime.UtcNow
        };
        _db.TaskAttachments.Add(a);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOne), new { id = a.Id }, a.ToDto());
    }

    /// <summary>Get a single attachment WITH its base64 data (for download/preview).</summary>
    [HttpGet("api/attachments/{id:int}")]
    public async Task<ActionResult<AttachmentDataDto>> GetOne(int id)
    {
        var a = await _db.TaskAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return a is null ? NotFound() : Ok(a.ToDataDto());
    }

    /// <summary>Stream an attachment's raw bytes with its content-type, so an
    /// &lt;img&gt;/&lt;video&gt; (or Image.network / VideoPlayer) can load it by URL.
    /// Range processing is enabled so video seeking works.</summary>
    [HttpGet("api/attachments/{id:int}/raw")]
    public async Task<IActionResult> GetRaw(int id)
    {
        var a = await _db.TaskAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(a.DataBase64); }
        catch { return NotFound(); }
        var mime = string.IsNullOrEmpty(a.MimeType) ? "application/octet-stream" : a.MimeType;
        return File(bytes, mime, enableRangeProcessing: true);
    }

    [HttpDelete("api/attachments/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.TaskAttachments.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        _db.TaskAttachments.Remove(a);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
