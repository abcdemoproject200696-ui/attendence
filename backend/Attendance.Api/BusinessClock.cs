namespace Attendance.Api;

/// <summary>
/// The business runs on a single fixed timezone (India / IST). The server, however,
/// may run in UTC (e.g. Render). Attendance math — especially the fixed lunch window
/// (13:00-14:00 etc.) — must use the BUSINESS wall-clock, not the server's UTC clock,
/// otherwise a 13:00 lunch shows up as 18:30. This converts any stored timestamp into
/// business-local wall-clock so the calculator works in IST everywhere.
/// </summary>
public static class BusinessClock
{
    /// <summary>India Standard Time (resolved by Windows id, then IANA id, then UTC fallback).</summary>
    public static readonly TimeZoneInfo Tz = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try next */ }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Return the business-local wall-clock for a stored timestamp.
    /// - Kind=Utc  (PostgreSQL/Render): convert UTC -> IST.
    /// - Kind=Unspecified/Local (SQLite local dev, already IST): keep as-is.
    /// Result Kind is Unspecified so it serialises WITHOUT an offset and the frontend
    /// shows it directly as the local clock time.
    /// </summary>
    public static DateTime ToLocal(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(dt, Tz), DateTimeKind.Unspecified)
            : DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    /// <summary>
    /// Treat an admin-typed time (e.g. "13:00" from the manual-punch form, which arrives
    /// Kind=Unspecified) as IST and store it the SAME way a live DateTime.Now punch is stored.
    /// Tag it Local so EF/Npgsql converts it to the correct instant (server TZ = IST), matching
    /// kiosk punches. Without this a manual 13:00 was assumed UTC and showed up as 18:30.
    /// </summary>
    public static DateTime AsLocalInput(DateTime wallClock) =>
        DateTime.SpecifyKind(wallClock, DateTimeKind.Local);
}
