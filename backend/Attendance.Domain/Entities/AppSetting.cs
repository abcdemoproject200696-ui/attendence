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
}
