using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

// Metadata only (no base64) — for the list.
public record EmployeeDocumentDto(int Id, int EmployeeId, string DocType, string FileName, string MimeType, bool Verified, string UploadedAt);
// Full document incl. base64 — for view/download.
public record EmployeeDocumentFullDto(int Id, string DocType, string FileName, string MimeType, string DataBase64);

public class EmployeeDocumentInputDto
{
    public string? DocType { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public string? DataBase64 { get; set; }
}

[ApiController]
[Route("api/employees")]
public class EmployeeDocumentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeDocumentsController(AppDbContext db) => _db = db;

    // ~8 MB file → base64 is ~4/3 bigger, so cap the base64 length at ~11 MB.
    private const int MaxBase64Length = 11_000_000;

    [HttpGet("{empId:int}/documents")]
    public async Task<ActionResult<IEnumerable<EmployeeDocumentDto>>> List(int empId)
    {
        var list = await _db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.EmployeeId == empId && d.IsActive)
            .OrderByDescending(d => d.Id)
            .ToListAsync();
        return Ok(list.Select(d => new EmployeeDocumentDto(
            d.Id, d.EmployeeId, d.DocType, d.FileName, d.MimeType, d.Verified,
            d.UploadedAt.ToString("yyyy-MM-dd"))));
    }

    [HttpPost("{empId:int}/documents")]
    public async Task<ActionResult<EmployeeDocumentDto>> Upload(int empId, EmployeeDocumentInputDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == empId))
            return BadRequest($"Employee {empId} does not exist.");
        var data = dto.DataBase64 ?? string.Empty;
        if (string.IsNullOrWhiteSpace(data)) return BadRequest("File data is required.");
        if (data.Length > MaxBase64Length) return BadRequest("File is too large (max ~8 MB).");

        var doc = new EmployeeDocument
        {
            EmployeeId = empId,
            DocType = string.IsNullOrWhiteSpace(dto.DocType) ? "Other" : dto.DocType.Trim(),
            FileName = (dto.FileName ?? "document").Trim(),
            MimeType = string.IsNullOrWhiteSpace(dto.MimeType) ? "application/octet-stream" : dto.MimeType.Trim(),
            DataBase64 = data,
            Verified = false,
            UploadedAt = DateTime.UtcNow,
        };
        _db.EmployeeDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return Ok(new EmployeeDocumentDto(doc.Id, doc.EmployeeId, doc.DocType, doc.FileName, doc.MimeType, doc.Verified,
            doc.UploadedAt.ToString("yyyy-MM-dd")));
    }

    [HttpGet("documents/{docId:int}")]
    public async Task<ActionResult<EmployeeDocumentFullDto>> Get(int docId)
    {
        var d = await _db.EmployeeDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == docId);
        if (d is null) return NotFound();
        return Ok(new EmployeeDocumentFullDto(d.Id, d.DocType, d.FileName, d.MimeType, d.DataBase64));
    }

    [HttpPut("documents/{docId:int}/verify")]
    public async Task<IActionResult> SetVerified(int docId, [FromQuery] bool verified = true)
    {
        var d = await _db.EmployeeDocuments.FirstOrDefaultAsync(x => x.Id == docId);
        if (d is null) return NotFound();
        d.Verified = verified;
        await _db.SaveChangesAsync();
        return Ok(new { d.Id, d.Verified });
    }

    // Deleting a specific document HARD-deletes it from the database (permanent).
    // (Deactivating an EMPLOYEE never touches documents — the employee row is only
    // marked inactive, never removed, so their documents are kept.)
    [HttpDelete("documents/{docId:int}")]
    public async Task<IActionResult> Delete(int docId)
    {
        var d = await _db.EmployeeDocuments.FirstOrDefaultAsync(x => x.Id == docId);
        if (d is null) return NotFound();
        _db.EmployeeDocuments.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
