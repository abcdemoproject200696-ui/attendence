namespace Attendance.Domain.Entities;

/// <summary>A KYC / joining document (image or PDF) attached to an employee.
/// Stored as base64 in the DB — same approach as task attachments.</summary>
public class EmployeeDocument
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    /// <summary>Aadhaar / PAN / Education / Experience / Bank / Photo / Offer / Other.</summary>
    public string DocType { get; set; } = "Other";
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string DataBase64 { get; set; } = string.Empty;
    public bool Verified { get; set; }
    /// <summary>Soft-delete flag — documents are never hard-deleted, only deactivated.</summary>
    public bool IsActive { get; set; } = true;
    public DateTime UploadedAt { get; set; }
}
