using Attendance.Domain;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

/// <summary>
/// Bridges the pure <see cref="AttendanceCalculator"/> with the database:
/// loads punches/holidays/leaves for a day, computes, and persists the AttendanceDay.
/// </summary>
public class AttendanceService
{
    private readonly AppDbContext _db;

    public AttendanceService(AppDbContext db) => _db = db;

    public const double DefaultFaceMatchThreshold = 0.5;

    /// <summary>Configured face-match threshold from AppSetting (falls back to default if missing).</summary>
    public async Task<double> GetFaceMatchThresholdAsync()
    {
        var settings = await _db.Settings.AsNoTracking().FirstOrDefaultAsync();
        return settings?.FaceMatchThreshold ?? DefaultFaceMatchThreshold;
    }

    /// <summary>Recompute and upsert the AttendanceDay for an employee on a date. Honours manual override.</summary>
    public async Task<AttendanceDay> RecomputeDayAsync(int employeeId, DateTime date)
    {
        date = date.Date;
        var employee = await _db.Employees
            .Include(e => e.Shift)
            .FirstOrDefaultAsync(e => e.Id == employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        var shift = employee.Shift ?? await _db.Shifts.FindAsync(employee.ShiftId)
            ?? throw new InvalidOperationException("Shift not found for employee.");

        var dayEntity = await _db.Days
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.Date == date);

        // If a manual override exists, keep its values; do not recompute.
        if (dayEntity is { IsManual: true })
        {
            return dayEntity;
        }

        var nextDay = date.AddDays(1);
        var punches = await _db.Punches
            .Where(p => p.EmployeeId == employeeId && p.Timestamp >= date && p.Timestamp < nextDay)
            .OrderBy(p => p.Timestamp)
            .ToListAsync();

        var context = await BuildContextAsync(employeeId, date, shift);
        var policy = ShiftPolicy.FromShift(shift);
        var calc = AttendanceCalculator.Compute(punches, policy, context);

        if (dayEntity == null)
        {
            dayEntity = new AttendanceDay { EmployeeId = employeeId, Date = date };
            _db.Days.Add(dayEntity);
        }

        ApplyCalc(dayEntity, calc);
        await _db.SaveChangesAsync();
        return dayEntity;
    }

    private static void ApplyCalc(AttendanceDay day, AttendanceCalculation calc)
    {
        day.FirstIn = calc.FirstIn;
        day.LastOut = calc.LastOut;
        day.GrossMinutes = calc.GrossMinutes;
        day.BreakMinutes = calc.BreakMinutes;
        day.LunchDeduction = calc.LunchDeduction;
        day.NetMinutes = calc.NetMinutes;
        day.Status = calc.Status;
        day.HasOpenSession = calc.HasOpenSession;
        day.IsManual = false;
    }

    public async Task<DayContext> BuildContextAsync(int employeeId, DateTime date, Shift shift)
    {
        date = date.Date;
        var isHoliday = await _db.Holidays.AnyAsync(h => h.Date == date);
        var isWeeklyOff = shift.WeeklyOffDays.Contains((int)date.DayOfWeek);
        var isLeave = await _db.Leaves.AnyAsync(l =>
            l.EmployeeId == employeeId &&
            l.Status == LeaveStatus.Approved &&
            l.FromDate <= date && l.ToDate >= date);

        return new DayContext
        {
            IsHoliday = isHoliday,
            IsWeeklyOff = isWeeklyOff,
            IsApprovedLeave = isLeave
        };
    }

    /// <summary>Compute (without persisting) an AttendanceDay for report purposes.</summary>
    public async Task<AttendanceDay> ComputeTransientAsync(Employee employee, Shift shift, DateTime date)
    {
        date = date.Date;
        var existing = await _db.Days.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EmployeeId == employee.Id && d.Date == date);
        if (existing is { IsManual: true })
        {
            existing.Employee = employee;
            return existing;
        }

        var nextDay = date.AddDays(1);
        var punches = await _db.Punches.AsNoTracking()
            .Where(p => p.EmployeeId == employee.Id && p.Timestamp >= date && p.Timestamp < nextDay)
            .OrderBy(p => p.Timestamp)
            .ToListAsync();

        var context = await BuildContextAsync(employee.Id, date, shift);
        var policy = ShiftPolicy.FromShift(shift);
        var calc = AttendanceCalculator.Compute(punches, policy, context);

        var day = new AttendanceDay { EmployeeId = employee.Id, Employee = employee, Date = date };
        ApplyCalc(day, calc);
        return day;
    }

    /// <summary>Result of a face match: the matched employee (null if none within threshold) and the nearest distance.</summary>
    public readonly record struct FaceMatch(Employee? Employee, double Distance);

    /// <summary>
    /// Find the nearest enrolled employee by Euclidean distance &lt; configured threshold.
    /// Returns the nearest distance regardless of match so callers can report confidence.
    /// </summary>
    public async Task<FaceMatch> MatchFaceAsync(IReadOnlyList<double> descriptor)
    {
        var threshold = await GetFaceMatchThresholdAsync();

        var candidates = await _db.Employees
            .Include(e => e.Shift)
            .Where(e => e.IsActive && e.FaceDescriptors != null)
            .ToListAsync();

        // Winner must be at least this much closer than the next-nearest DIFFERENT
        // person, otherwise the scan is ambiguous and we reject it (NON-EMPLOYEE).
        // This prevents matching a lookalike just because they were marginally nearest.
        const double margin = 0.05;

        Employee? best = null;
        var bestDist = double.MaxValue;   // nearest employee's distance
        var secondDist = double.MaxValue; // nearest distance among the OTHER employees

        foreach (var c in candidates)
        {
            if (c.FaceDescriptors is not { Count: > 0 }) continue;

            // An employee may have up to 5 enrolled descriptors; take the MIN distance
            // (match if the live face is near ANY one of them).
            var empMin = double.MaxValue;
            foreach (var stored in c.FaceDescriptors)
            {
                if (stored is null || stored.Count != descriptor.Count) continue;
                var d = EuclideanDistance(stored, descriptor);
                if (d < empMin) empMin = d;
            }

            if (empMin < bestDist)
            {
                secondDist = bestDist; // old best becomes runner-up
                bestDist = empMin;
                best = c;
            }
            else if (empMin < secondDist)
            {
                secondDist = empMin;
            }
        }

        // Confident match only if: within threshold AND clearly closer than the runner-up.
        var confident = best is not null
                        && bestDist < threshold
                        && (secondDist - bestDist) >= margin;

        return confident ? new FaceMatch(best, bestDist) : new FaceMatch(null, bestDist);
    }

    private static double EuclideanDistance(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double sum = 0;
        for (var i = 0; i < a.Count; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum);
    }
}
