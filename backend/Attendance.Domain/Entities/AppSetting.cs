namespace Attendance.Domain.Entities;

/// <summary>Single-row global application settings (admin-editable).</summary>
public class AppSetting
{
    public int Id { get; set; }

    /// <summary>Face match max Euclidean distance (lower = stricter). Range 0.3..0.7.</summary>
    public double FaceMatchThreshold { get; set; } = 0.5;

    /// <summary>Require blink (liveness) before punch on kiosk — anti photo-spoof. Client-side enforced.</summary>
    public bool RequireLiveness { get; set; }

    /// <summary>Speak a greeting aloud on the kiosk when someone punches in/out.</summary>
    public bool VoiceEnabled { get; set; } = true;

    /// <summary>
    /// When true, salary pays for ALL worked hours including overtime (e.g. 10h on an 8h
    /// shift = 10/8 of a day's pay). When false (default), each day is capped at a full
    /// day (8h) — under-time is pro-rated down, over-time is NOT paid extra.
    /// </summary>
    public bool OvertimePayable { get; set; }

    /// <summary>
    /// When true, HR (roleId 2) may manually add/edit/delete punches and override days.
    /// Admin (roleId 1) can always edit regardless of this flag.
    /// </summary>
    public bool HrCanEditAttendance { get; set; }

    /// <summary>When true, email the assignee whenever a task is assigned to them
    /// (needs SMTP_USER / SMTP_PASS env vars set). Off by default.</summary>
    public bool TaskAssignEmail { get; set; }

    /// <summary>When true, self-signup requires an email OTP: the app emails a code,
    /// the user must enter it, and only a matching code completes registration.
    /// When false, signup completes immediately (current behaviour). Needs SMTP.</summary>
    public bool SignupOtpEmail { get; set; }
}
