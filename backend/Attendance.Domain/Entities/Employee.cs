namespace Attendance.Domain.Entities;

public class Employee
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    public int ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public decimal MonthlySalary { get; set; }

    public bool IsActive { get; set; } = true;
    public string? PhotoUrl { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Dob { get; set; }

    /// <summary>SHA-256 hex of the login password. NEVER exposed in any DTO.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>1..5 face embeddings (each 128-d). Stored in DB as JSON, never leaked in list DTOs.</summary>
    public List<List<double>>? FaceDescriptors { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool HasFace => FaceDescriptors is { Count: > 0 };

    public int FaceCount => FaceDescriptors?.Count ?? 0;
}
