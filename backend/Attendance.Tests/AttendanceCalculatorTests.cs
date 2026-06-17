using Attendance.Domain;
using Attendance.Domain.Entities;
using Xunit;

namespace Attendance.Tests;

public class AttendanceCalculatorTests
{
    // 2026-06-15 is a Monday (not Sunday), good neutral working day.
    private static readonly DateTime Day = new(2026, 6, 15);

    private static AttendancePunch P(string hhmm, Direction dir)
    {
        var parts = hhmm.Split(':');
        var ts = Day.AddHours(int.Parse(parts[0])).AddMinutes(int.Parse(parts[1]));
        return new AttendancePunch { Timestamp = ts, Direction = dir };
    }

    private static AttendancePunch P(DateTime ts, Direction dir) =>
        new() { Timestamp = ts, Direction = dir };

    private static ShiftPolicy Policy(
        bool autoLunch = true,
        int required = 480,
        int halfDay = 240,
        int lunchStart = 13 * 60,
        int lunchEnd = 14 * 60) => new()
    {
        AutoDeductLunch = autoLunch,
        RequiredMinutes = required,
        HalfDayThresholdMinutes = halfDay,
        LunchStartMinutes = lunchStart,
        LunchEndMinutes = lunchEnd,
        WeeklyOffDays = new[] { 0 }
    };

    private static DayContext Ctx(bool holiday = false, bool weeklyOff = false, bool leave = false) =>
        new() { IsHoliday = holiday, IsWeeklyOff = weeklyOff, IsApprovedLeave = leave };

    // ---------- Step 2/6: simple in/out ----------
    [Fact]
    public void SimpleInOut_NoLunchOverlap_NetEqualsGross()
    {
        // 10:00 -> 12:00 = 120 min, no lunch overlap (lunch 13-14).
        var punches = new[] { P("10:00", Direction.IN), P("12:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(120, r.GrossMinutes);
        Assert.Equal(0, r.LunchDeduction);
        Assert.Equal(120, r.NetMinutes);
        Assert.False(r.HasOpenSession);
        Assert.Equal(Day.AddHours(10), r.FirstIn);
        Assert.Equal(Day.AddHours(12), r.LastOut);
    }

    // ---------- Multiple sessions: washroom break auto-excluded from gross ----------
    [Fact]
    public void MultipleSessions_BreakExcludedFromGross()
    {
        // Session1 10:00-11:00 (60), washroom out 11:00-11:10, session2 11:10-12:00 (50) => gross 110.
        var punches = new[]
        {
            P("10:00", Direction.IN), P("11:00", Direction.OUT),
            P("11:10", Direction.IN), P("12:00", Direction.OUT)
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(110, r.GrossMinutes);
        // span = 120, gross = 110 => break = 10 (the washroom gap).
        Assert.Equal(10, r.BreakMinutes);
        Assert.Equal(110, r.NetMinutes);
    }

    // ---------- Lunch auto-deduct overlap ----------
    [Fact]
    public void LunchAutoDeduct_FullOverlap_Deducts60()
    {
        // 10:00-19:00 spans lunch 13-14 fully => deduct 60. gross 540, net 480.
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(540, r.GrossMinutes);
        Assert.Equal(60, r.LunchDeduction);
        Assert.Equal(480, r.NetMinutes);
        Assert.Equal(DayStatus.Present, r.Status);
        // The exact deducted window is exposed so the UI/PDF can show "13:00 - 14:00".
        Assert.Equal(Day.AddHours(13), r.LunchFrom);
        Assert.Equal(Day.AddHours(14), r.LunchTo);
    }

    [Fact]
    public void LunchAutoDeduct_PartialOverlap_DeductsOnlyOverlap()
    {
        // 13:30-14:30 overlaps lunch only 13:30-14:00 => 30 min deducted.
        var punches = new[] { P("13:30", Direction.IN), P("14:30", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(60, r.GrossMinutes);
        Assert.Equal(30, r.LunchDeduction);
        Assert.Equal(30, r.NetMinutes);
        Assert.Equal(Day.AddHours(13).AddMinutes(30), r.LunchFrom); // overlap starts at check-in 13:30
        Assert.Equal(Day.AddHours(14), r.LunchTo);                  // ...to lunch end 14:00
    }

    [Fact]
    public void NoLunchOverlap_LunchWindowIsNull()
    {
        // 10:00-12:00 doesn't touch lunch (13-14) => nothing deducted, no window.
        var punches = new[] { P("10:00", Direction.IN), P("12:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());
        Assert.Equal(0, r.LunchDeduction);
        Assert.Null(r.LunchFrom);
        Assert.Null(r.LunchTo);
    }

    // ---------- No double-deduct when already punched out during lunch ----------
    [Fact]
    public void LunchNoDoubleDeduct_WhenPunchedOutDuringLunch()
    {
        // Worker punches OUT for lunch 13:00, back IN 14:00. No session overlaps lunch window.
        var punches = new[]
        {
            P("10:00", Direction.IN), P("13:00", Direction.OUT),  // 180 min
            P("14:00", Direction.IN), P("19:00", Direction.OUT)   // 300 min
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(480, r.GrossMinutes);
        Assert.Equal(0, r.LunchDeduction); // not deducted again
        Assert.Equal(480, r.NetMinutes);
        Assert.Equal(DayStatus.Present, r.Status);
    }

    // ---------- Open session (missing OUT) flagged ----------
    [Fact]
    public void OpenSession_LastInWithoutOut_Flagged_NotInGross()
    {
        // 10:00 IN, 12:00 OUT (120), 13:00 IN (open).
        var punches = new[]
        {
            P("10:00", Direction.IN), P("12:00", Direction.OUT),
            P("13:00", Direction.IN)
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.True(r.HasOpenSession);
        Assert.Equal(120, r.GrossMinutes); // open session not added
        // Currently checked in -> Present even though net (120) is below half-day threshold.
        Assert.Equal(DayStatus.Present, r.Status);
    }

    // ---------- Present-on-check-in: open session with low hours ----------
    [Fact]
    public void OpenSession_LowHours_StatusPresent()
    {
        // Just punched IN at 10:00, no OUT yet. Net is 0 but employee is on the clock.
        var punches = new[] { P("10:00", Direction.IN) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.True(r.HasOpenSession);
        Assert.Equal(0, r.NetMinutes);
        Assert.Equal(DayStatus.Present, r.Status);
    }

    // ---------- Closed day with full hours -> Present still works ----------
    [Fact]
    public void ClosedDay_FullHours_StatusPresent()
    {
        // 10:00-19:00, lunch deducted -> net 480 == required, day closed (last punch OUT).
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.False(r.HasOpenSession);
        Assert.Equal(480, r.NetMinutes);
        Assert.Equal(DayStatus.Present, r.Status);
    }

    // ---------- Closed day low hours -> HalfDay / Absent as before ----------
    [Fact]
    public void ClosedDay_LowHours_HalfDayOrAbsent()
    {
        // Closed half-day: net 240 == threshold -> HalfDay.
        var half = new[] { P("10:00", Direction.IN), P("15:00", Direction.OUT) };
        var rHalf = AttendanceCalculator.Compute(half, Policy(), Ctx());
        Assert.False(rHalf.HasOpenSession);
        Assert.Equal(240, rHalf.NetMinutes);
        Assert.Equal(DayStatus.HalfDay, rHalf.Status);

        // Closed below threshold: net 120 -> Absent.
        var absent = new[] { P("10:00", Direction.IN), P("12:00", Direction.OUT) };
        var rAbsent = AttendanceCalculator.Compute(absent, Policy(), Ctx());
        Assert.False(rAbsent.HasOpenSession);
        Assert.Equal(120, rAbsent.NetMinutes);
        Assert.Equal(DayStatus.Absent, rAbsent.Status);
    }

    // ---------- Debounce duplicate same-direction punches < 60s ----------
    [Fact]
    public void Debounce_DuplicateSameDirectionWithin60s_Ignored()
    {
        var inTs = Day.AddHours(10);
        var punches = new[]
        {
            P(inTs, Direction.IN),
            P(inTs.AddSeconds(30), Direction.IN),  // duplicate -> ignored
            P(Day.AddHours(12), Direction.OUT)
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        // Should pair the first IN (10:00) with OUT (12:00) => 120, not the 10:00:30 one.
        Assert.Equal(120, r.GrossMinutes);
        Assert.Equal(inTs, r.FirstIn);
        Assert.False(r.HasOpenSession);
    }

    [Fact]
    public void Debounce_SameDirectionBeyond60s_NotIgnored()
    {
        var inTs = Day.AddHours(10);
        var punches = new[]
        {
            P(inTs, Direction.IN),
            P(inTs.AddSeconds(90), Direction.IN),  // > 60s, kept -> but no OUT between, open logic
            P(Day.AddHours(12), Direction.OUT)
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        // Two consecutive INs kept; pairing uses earliest open IN -> 10:00 -> 12:00 = 120.
        Assert.Equal(120, r.GrossMinutes);
        Assert.False(r.HasOpenSession);
    }

    // ---------- Half-day threshold ----------
    [Fact]
    public void HalfDay_WhenNetBetweenThresholdAndRequired()
    {
        // 10:00-15:00 = 300 gross, lunch overlap 60 => net 240 == halfDay threshold => HalfDay.
        var punches = new[] { P("10:00", Direction.IN), P("15:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(required: 480, halfDay: 240), Ctx());

        Assert.Equal(240, r.NetMinutes);
        Assert.Equal(DayStatus.HalfDay, r.Status);
    }

    // ---------- Absent threshold ----------
    [Fact]
    public void Absent_WhenNetBelowHalfDayThreshold()
    {
        // 10:00-12:00 = 120 net, below 240 => Absent.
        var punches = new[] { P("10:00", Direction.IN), P("12:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());

        Assert.Equal(120, r.NetMinutes);
        Assert.Equal(DayStatus.Absent, r.Status);
    }

    [Fact]
    public void Absent_WhenNoPunches()
    {
        var r = AttendanceCalculator.Compute(Array.Empty<AttendancePunch>(), Policy(), Ctx());
        Assert.Equal(0, r.NetMinutes);
        Assert.Equal(DayStatus.Absent, r.Status);
        Assert.False(r.HasOpenSession);
    }

    // ---------- Status precedence: holiday / weeklyOff / leave ----------
    [Fact]
    public void Holiday_TakesPrecedence_EvenWithFullWork()
    {
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx(holiday: true));
        Assert.Equal(DayStatus.Holiday, r.Status);
    }

    [Fact]
    public void WeeklyOff_TakesPrecedenceOverWork_ButBelowHoliday()
    {
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx(weeklyOff: true));
        Assert.Equal(DayStatus.WeeklyOff, r.Status);

        // Holiday should still win over weeklyOff.
        var r2 = AttendanceCalculator.Compute(punches, Policy(), Ctx(holiday: true, weeklyOff: true));
        Assert.Equal(DayStatus.Holiday, r2.Status);
    }

    [Fact]
    public void ApprovedLeave_SetsLeaveStatus_WhenNoHolidayOrWeeklyOff()
    {
        var r = AttendanceCalculator.Compute(Array.Empty<AttendancePunch>(), Policy(), Ctx(leave: true));
        Assert.Equal(DayStatus.Leave, r.Status);

        // weeklyOff outranks leave.
        var r2 = AttendanceCalculator.Compute(Array.Empty<AttendancePunch>(), Policy(), Ctx(weeklyOff: true, leave: true));
        Assert.Equal(DayStatus.WeeklyOff, r2.Status);
    }

    // ---------- autoDeductLunch=false ----------
    [Fact]
    public void NoAutoDeductLunch_NetEqualsGross()
    {
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, Policy(autoLunch: false), Ctx());
        Assert.Equal(540, r.GrossMinutes);
        Assert.Equal(0, r.LunchDeduction);
        Assert.Equal(540, r.NetMinutes);
    }

    // ========== Real-world 2 PM - 3 PM lunch cases (user scenarios) ==========
    private static ShiftPolicy LunchTwoToThree(bool autoLunch = true) => Policy(
        autoLunch: autoLunch, required: 480, halfDay: 240,
        lunchStart: 14 * 60, lunchEnd: 15 * 60);

    [Fact]
    public void Lunch2to3_BackInDuringLunch_LunchHourStillExcluded()
    {
        // IN 10:00, OUT 14:00 (lunch), back IN 14:20, OUT 19:00.
        // Morning 4h + afternoon 14:20-19:00 (4h40m) = 8h40m gross; only the 14:20-15:00
        // slice (40m) falls in lunch and is deducted => net exactly 8h.
        var punches = new[]
        {
            P("10:00", Direction.IN), P("14:00", Direction.OUT),
            P("14:20", Direction.IN), P("19:00", Direction.OUT),
        };
        var r = AttendanceCalculator.Compute(punches, LunchTwoToThree(), Ctx());

        Assert.Equal(520, r.GrossMinutes);
        Assert.Equal(40, r.LunchDeduction);
        Assert.Equal(480, r.NetMinutes); // 8h
        Assert.Equal(DayStatus.Present, r.Status);
    }

    [Fact]
    public void Lunch2to3_StraightThrough_DeductsFullHour()
    {
        // IN 10:00 -> OUT 19:00 = 9h; lunch 1h => 8h. Window shown is 14:00-15:00.
        var punches = new[] { P("10:00", Direction.IN), P("19:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, LunchTwoToThree(), Ctx());

        Assert.Equal(540, r.GrossMinutes);
        Assert.Equal(60, r.LunchDeduction);
        Assert.Equal(480, r.NetMinutes);
        Assert.Equal(Day.AddHours(14), r.LunchFrom);
        Assert.Equal(Day.AddHours(15), r.LunchTo);
    }

    [Fact]
    public void Lunch2to3_Overtime_ExtraHoursShowInNet()
    {
        // IN 10:00 -> OUT 21:00 = 11h; lunch 1h => 10h net (2h overtime visible).
        var punches = new[] { P("10:00", Direction.IN), P("21:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, LunchTwoToThree(), Ctx());

        Assert.Equal(660, r.GrossMinutes);
        Assert.Equal(60, r.LunchDeduction);
        Assert.Equal(600, r.NetMinutes); // 10h
    }

    [Fact]
    public void Lunch2to3_LeavesBeforeLunch_NoDeduction()
    {
        // Half-day: IN 10:00 -> OUT 13:00 (leaves before 2 PM lunch) => no lunch cut.
        var punches = new[] { P("10:00", Direction.IN), P("13:00", Direction.OUT) };
        var r = AttendanceCalculator.Compute(punches, LunchTwoToThree(), Ctx());

        Assert.Equal(180, r.GrossMinutes);
        Assert.Equal(0, r.LunchDeduction);
        Assert.Null(r.LunchFrom);
        Assert.Equal(180, r.NetMinutes);
    }

    // ---------- Unsorted input still computed correctly ----------
    [Fact]
    public void UnsortedPunches_SortedBeforeComputing()
    {
        var punches = new[]
        {
            P("19:00", Direction.OUT),
            P("10:00", Direction.IN)
        };
        var r = AttendanceCalculator.Compute(punches, Policy(), Ctx());
        Assert.Equal(540, r.GrossMinutes);
        Assert.Equal(480, r.NetMinutes);
    }
}
