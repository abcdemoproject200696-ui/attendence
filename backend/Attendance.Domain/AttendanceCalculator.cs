using Attendance.Domain.Entities;

namespace Attendance.Domain;

/// <summary>
/// PURE attendance computation core. No EF / DB dependency.
/// Implements the CONTRACT.md steps exactly.
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>Same-direction punches closer than this are treated as duplicates (debounced).</summary>
    public const int DebounceSeconds = 60;

    /// <summary>
    /// Compute a day's attendance from its raw punches + shift policy + day context.
    /// </summary>
    public static AttendanceCalculation Compute(
        IEnumerable<AttendancePunch> punches,
        ShiftPolicy policy,
        DayContext context)
    {
        // Step 1: sort by timestamp, then debounce same-direction punches < 60s apart.
        var sorted = punches.OrderBy(p => p.Timestamp).ToList();
        var debounced = Debounce(sorted);

        // Step 2 + 3: pair IN -> OUT sessions, sum gross, detect open session.
        var (sessions, hasOpenSession) = PairSessions(debounced);

        var grossMinutes = sessions.Sum(s => (int)Math.Round((s.Out - s.In).TotalMinutes));

        DateTime? firstIn = debounced.FirstOrDefault(p => p.Direction == Direction.IN)?.Timestamp;
        DateTime? lastOut = debounced.LastOrDefault(p => p.Direction == Direction.OUT)?.Timestamp;

        // Step 4: breakMinutes = (lastOut - firstIn) - grossMinutes (display only).
        var breakMinutes = 0;
        if (firstIn.HasValue && lastOut.HasValue && lastOut.Value > firstIn.Value)
        {
            var span = (int)Math.Round((lastOut.Value - firstIn.Value).TotalMinutes);
            breakMinutes = Math.Max(0, span - grossMinutes);
        }

        // Step 5: lunch auto-deduct = sum of overlap(session, lunch window).
        var lunchDeduction = 0;
        if (policy.AutoDeductLunch)
        {
            foreach (var s in sessions)
            {
                lunchDeduction += LunchOverlapMinutes(s.In, s.Out, policy);
            }
        }

        // Step 6: net = gross - lunchDeduction.
        var netMinutes = grossMinutes - lunchDeduction;
        if (netMinutes < 0) netMinutes = 0;

        // Step 7: status precedence (present-on-check-in aware).
        var status = ResolveStatus(netMinutes, firstIn.HasValue, hasOpenSession, policy, context);

        return new AttendanceCalculation
        {
            FirstIn = firstIn,
            LastOut = lastOut,
            GrossMinutes = grossMinutes,
            BreakMinutes = breakMinutes,
            LunchDeduction = lunchDeduction,
            NetMinutes = netMinutes,
            Status = status,
            HasOpenSession = hasOpenSession
        };
    }

    private static List<AttendancePunch> Debounce(List<AttendancePunch> sorted)
    {
        var result = new List<AttendancePunch>();
        foreach (var p in sorted)
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Direction == p.Direction &&
                    (p.Timestamp - last.Timestamp).TotalSeconds < DebounceSeconds)
                {
                    // duplicate same-direction within debounce window -> ignore.
                    continue;
                }
            }
            result.Add(p);
        }
        return result;
    }

    private readonly record struct Session(DateTime In, DateTime Out);

    private static (List<Session> Sessions, bool HasOpenSession) PairSessions(List<AttendancePunch> punches)
    {
        var sessions = new List<Session>();
        DateTime? openIn = null;

        foreach (var p in punches)
        {
            if (p.Direction == Direction.IN)
            {
                // If already have an open IN (consecutive INs after debounce keeps first),
                // keep the earliest open IN — only update if none open.
                openIn ??= p.Timestamp;
            }
            else // OUT
            {
                if (openIn.HasValue)
                {
                    if (p.Timestamp > openIn.Value)
                    {
                        sessions.Add(new Session(openIn.Value, p.Timestamp));
                    }
                    openIn = null;
                }
                // OUT with no open IN -> stray, ignore.
            }
        }

        // Step 3: last punch was IN without OUT -> open session (not added to gross).
        var hasOpenSession = openIn.HasValue;
        return (sessions, hasOpenSession);
    }

    /// <summary>
    /// Overlap (in minutes) between a session and the lunch window of its calendar day.
    /// </summary>
    private static int LunchOverlapMinutes(DateTime sessionIn, DateTime sessionOut, ShiftPolicy policy)
    {
        var dayStart = sessionIn.Date;
        var lunchStart = dayStart.AddMinutes(policy.LunchStartMinutes);
        var lunchEnd = dayStart.AddMinutes(policy.LunchEndMinutes);

        var overlapStart = sessionIn > lunchStart ? sessionIn : lunchStart;
        var overlapEnd = sessionOut < lunchEnd ? sessionOut : lunchEnd;

        if (overlapEnd <= overlapStart) return 0;
        return (int)Math.Round((overlapEnd - overlapStart).TotalMinutes);
    }

    private static DayStatus ResolveStatus(
        int netMinutes, bool hasPunches, bool hasOpenSession, ShiftPolicy policy, DayContext context)
    {
        // No punches: holiday > weeklyOff > approved leave > Absent.
        if (!hasPunches)
        {
            if (context.IsHoliday) return DayStatus.Holiday;
            if (context.IsWeeklyOff) return DayStatus.WeeklyOff;
            if (context.IsApprovedLeave) return DayStatus.Leave;
            return DayStatus.Absent;
        }

        // Punches exist: still honour holiday/weeklyOff/leave precedence (a worked
        // holiday/off/leave day keeps that classification, matching prior behaviour).
        if (context.IsHoliday) return DayStatus.Holiday;
        if (context.IsWeeklyOff) return DayStatus.WeeklyOff;
        if (context.IsApprovedLeave) return DayStatus.Leave;

        // Currently checked in (last punch IN, no closing OUT) -> Present regardless of hours.
        if (hasOpenSession) return DayStatus.Present;

        // Day closed: worked-time thresholds decide.
        if (netMinutes >= policy.RequiredMinutes) return DayStatus.Present;
        if (netMinutes >= policy.HalfDayThresholdMinutes) return DayStatus.HalfDay;
        return DayStatus.Absent;
    }
}
