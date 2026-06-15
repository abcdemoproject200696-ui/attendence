using Attendance.Domain.Entities;

namespace Attendance.Domain;

/// <summary>
/// Plain, DB-free snapshot of the shift rules the calculator needs.
/// Times of day are minutes-from-midnight to keep the calculator pure and simple.
/// </summary>
public sealed class ShiftPolicy
{
    public int RequiredMinutes { get; init; } = 480;
    public int HalfDayThresholdMinutes { get; init; } = 240;

    public bool AutoDeductLunch { get; init; } = true;
    public bool LunchPaid { get; init; }

    /// <summary>Lunch window start, minutes from midnight (e.g. 13:00 => 780).</summary>
    public int LunchStartMinutes { get; init; } = 13 * 60;

    /// <summary>Lunch window end, minutes from midnight (e.g. 14:00 => 840).</summary>
    public int LunchEndMinutes { get; init; } = 14 * 60;

    /// <summary>0=Sunday .. 6=Saturday.</summary>
    public IReadOnlyCollection<int> WeeklyOffDays { get; init; } = new[] { 0 };

    public static int ParseTimeToMinutes(string hhmm)
    {
        var parts = hhmm.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }

    public static ShiftPolicy FromShift(Shift shift) => new()
    {
        RequiredMinutes = shift.RequiredMinutes,
        HalfDayThresholdMinutes = shift.HalfDayThresholdMinutes,
        AutoDeductLunch = shift.AutoDeductLunch,
        LunchPaid = shift.LunchPaid,
        LunchStartMinutes = ParseTimeToMinutes(shift.LunchStart),
        LunchEndMinutes = ParseTimeToMinutes(shift.LunchEnd),
        WeeklyOffDays = shift.WeeklyOffDays.ToArray()
    };
}
