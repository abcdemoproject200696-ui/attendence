namespace Attendance.Api;

/// <summary>
/// The business runs on a single fixed timezone (India / IST), but timestamps are stored
/// as absolute UTC instants so the result is identical whether the server runs in UTC
/// (Render) or IST (a dev laptop). Convert to IST only at the edges: when displaying, and
/// when reading an admin-typed wall-clock time.
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

    /// <summary>Current IST wall-clock, derived from the absolute UTC instant (machine-TZ independent).</summary>
    public static DateTime Now => ToLocal(DateTime.UtcNow);

    /// <summary>Today's calendar date in IST.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>
    /// A stored UTC instant -> IST wall-clock (Kind=Unspecified) for display + calculation.
    /// The stored value is ALWAYS treated as UTC, regardless of the Kind the DB hands back
    /// (PostgreSQL legacy + SQLite both return Unspecified).
    /// </summary>
    public static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz),
            DateTimeKind.Unspecified);

    /// <summary>
    /// An IST wall-clock time (e.g. admin-typed "13:00", or an IST calendar date) -> the
    /// absolute UTC instant to store / to compare against stored timestamps.
    /// </summary>
    public static DateTime ToUtc(DateTime istWallClock) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(istWallClock, DateTimeKind.Unspecified), Tz),
            DateTimeKind.Utc);
}
