using System.ComponentModel.DataAnnotations.Schema;

namespace Attendance.Domain.Entities;

public class Employee
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    // Full display name is now DERIVED from First + Last (no DB column). All screens
    // that read Name keep working; it's always in sync and never needs manual update.
    [NotMapped]
    public string Name => string.Join(" ", new[] { FirstName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

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

    // ---- Job / onboarding details ----
    public string? Designation { get; set; }
    public string? Department { get; set; }
    public string? DateOfJoining { get; set; }

    // ---- KYC / statutory ----
    public string? Aadhaar { get; set; }
    public string? Pan { get; set; }
    public string? UanPf { get; set; }

    // ---- Bank (salary) ----
    public string? BankAccount { get; set; }
    public string? Ifsc { get; set; }
    public string? BankName { get; set; }

    // ---- Contact / address ----
    public string? EmergencyName { get; set; }
    public string? EmergencyPhone { get; set; }
    public string? CurrentAddress { get; set; }
    public string? PermanentAddress { get; set; }

    /// <summary>SHA-256 hex of the login password. NEVER exposed in any DTO.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>1..5 face embeddings (each 128-d). Stored in DB as JSON, never leaked in list DTOs.</summary>
    public List<List<double>>? FaceDescriptors { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool HasFace => FaceDescriptors is { Count: > 0 };

    public int FaceCount => FaceDescriptors?.Count ?? 0;
}
