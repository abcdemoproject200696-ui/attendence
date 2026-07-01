namespace Attendance.Domain.Entities;

/// <summary>
/// A one-time signup verification code, stored so it survives a server restart
/// (Render free tier spins down). One row per email — a fresh request overwrites
/// the old code. The row is deleted once the signup completes.
/// </summary>
public class SignupOtp
{
    public int Id { get; set; }
    /// <summary>Lower-cased recipient email the code was sent to.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>The 6-digit code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>When it was issued (used to expire old codes).</summary>
    public DateTime CreatedAt { get; set; }
}
