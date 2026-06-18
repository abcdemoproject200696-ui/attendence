using System.Globalization;
using Attendance.Api.Dtos;
using Attendance.Api.Services;
using Attendance.Domain;
using Attendance.Domain.Entities;
using Attendance.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Controllers;

[ApiController]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttendanceService _svc;

    public AttendanceController(AppDbContext db, AttendanceService svc)
    {
        _db = db;
        _svc = svc;
    }

    // -------------------- PUNCH --------------------
    [HttpPost("punch")]
    public async Task<ActionResult<PunchResultDto>> Punch(PunchRequestDto req)
    {
        Employee? employee = null;
        double? matchDistance = null;
        int? matchConfidence = null;

        if (req.EmployeeId.HasValue)
        {
            employee = await _db.Employees.Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.Id == req.EmployeeId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(req.EmployeeCode))
        {
            // Code matched case-INSENSITIVELY (emp001 == EMP001).
            var reqCode = req.EmployeeCode.Trim().ToUpperInvariant();
            employee = await _db.Employees.Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.Code.ToUpper() == reqCode);
        }
        else if (req.FaceDescriptor is { Count: > 0 })
        {
            var threshold = await _svc.GetFaceMatchThresholdAsync();
            var match = await _svc.MatchFaceAsync(req.FaceDescriptor);
            employee = match.Employee;
            if (employee is null)
                return NotFound(new { message = "No matching face found. Please enroll or use code." });

            matchDistance = Math.Round(match.Distance, 3);
            matchConfidence = (int)Math.Round(
                Math.Clamp((threshold - match.Distance) / threshold, 0, 1) * 100);
        }

        if (employee is null)
            return NotFound(new { message = "Employee not found. Provide employeeId, employeeCode, or a matching faceDescriptor." });

        var nowUtc = DateTime.UtcNow;               // store the absolute instant
        var istToday = BusinessClock.Today;         // IST calendar day
        var startUtc = BusinessClock.ToUtc(istToday);
        var endUtc = BusinessClock.ToUtc(istToday.AddDays(1));

        var lastPunch = await _db.Punches
            .Where(p => p.EmployeeId == employee.Id && p.Timestamp >= startUtc && p.Timestamp < endUtc)
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefaultAsync();

        // Decide direction from last punch of the day: none/last OUT -> IN, last IN -> OUT.
        var direction = lastPunch is { Direction: Direction.IN } ? Direction.OUT : Direction.IN;

        var punch = new AttendancePunch
        {
            EmployeeId = employee.Id,
            Timestamp = nowUtc,
            Direction = direction,
            DeviceId = req.DeviceId,
            Source = req.Source,
            Note = null
        };
        _db.Punches.Add(punch);
        await _db.SaveChangesAsync();

        var day = await _svc.RecomputeDayAsync(employee.Id, istToday);

        punch.Employee = employee;
        var message = $"{employee.Name} punched {direction} at {BusinessClock.ToLocal(nowUtc):HH:mm}.";
        return Ok(new PunchResultDto(punch.ToDto(), day.NetMinutes, message, matchDistance, matchConfidence));
    }

    // -------------------- TODAY --------------------
    [HttpGet("today/{employeeId:int}")]
    public async Task<ActionResult<AttendanceDayDto>> Today(int employeeId)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var day = await _svc.RecomputeDayAsync(employeeId, BusinessClock.Today);
        await _db.Entry(day).Reference(d => d.Employee).LoadAsync();
        return Ok(day.ToDto());
    }

    // -------------------- DAILY (all employees) --------------------
    [HttpGet("daily")]
    public async Task<ActionResult<IEnumerable<AttendanceDayDto>>> Daily([FromQuery] string date)
    {
        if (!TryParseDate(date, out var d)) return BadRequest("date must be yyyy-MM-dd.");

        var employees = await _db.Employees.Include(e => e.Shift)
            .Where(e => e.IsActive).OrderBy(e => e.Code).ToListAsync();

        var result = new List<AttendanceDayDto>();
        foreach (var e in employees)
        {
            var shift = e.Shift ?? await _db.Shifts.FindAsync(e.ShiftId);
            if (shift is null) continue;
            var day = await _svc.ComputeTransientAsync(e, shift, d);
            result.Add(day.ToDto());
        }
        return Ok(result);
    }

    // -------------------- RANGE (one employee, many dates) --------------------
    // Daily Attendance page filter: ek employee ki "from".."to" tak har din ki row.
    [HttpGet("range")]
    public async Task<ActionResult<IEnumerable<AttendanceDayDto>>> Range(
        [FromQuery] int employeeId, [FromQuery] string from, [FromQuery] string to)
    {
        if (!TryParseDate(from, out var start)) return BadRequest("from must be yyyy-MM-dd.");
        if (!TryParseDate(to, out var end)) return BadRequest("to must be yyyy-MM-dd.");
        if (end < start) return BadRequest("'to' must be on/after 'from'.");
        // Guard against accidentally huge ranges.
        if ((end - start).TotalDays > 366) return BadRequest("Range too large (max 366 days).");

        var employee = await _db.Employees.Include(e => e.Shift)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return NotFound();
        var shift = employee.Shift ?? await _db.Shifts.FindAsync(employee.ShiftId);
        if (shift is null) return BadRequest("Employee has no shift.");

        var result = new List<AttendanceDayDto>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var day = await _svc.ComputeTransientAsync(employee, shift, date);
            result.Add(day.ToDto());
        }
        return Ok(result);
    }

    // -------------------- RECOMPUTE --------------------
    [HttpPost("recompute")]
    public async Task<ActionResult<AttendanceDayDto>> Recompute(
        [FromQuery] string date, [FromQuery] int employeeId)
    {
        if (!TryParseDate(date, out var d)) return BadRequest("date must be yyyy-MM-dd.");
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();

        var day = await _svc.RecomputeDayAsync(employeeId, d);
        await _db.Entry(day).Reference(x => x.Employee).LoadAsync();
        return Ok(day.ToDto());
    }

    // -------------------- MONTHLY REPORT --------------------
    [HttpGet("report")]
    public async Task<ActionResult<MonthlyReportDto>> Report(
        [FromQuery] string month, [FromQuery] int employeeId)
    {
        if (!TryParseMonth(month, out var first)) return BadRequest("month must be yyyy-MM.");

        var employee = await _db.Employees.Include(e => e.Shift)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return NotFound();
        var shift = employee.Shift ?? await _db.Shifts.FindAsync(employee.ShiftId);
        if (shift is null) return BadRequest("Employee has no shift.");

        var overtimePayable = (await _db.Settings.AsNoTracking().FirstOrDefaultAsync())?.OvertimePayable ?? false;
        var (dayDtos, summary) = await ComputeMonthAsync(employee, shift, first, overtimePayable);
        var report = new MonthlyReportDto(
            employee.Id, employee.Name, first.ToString("yyyy-MM"), dayDtos, summary);
        return Ok(report);
    }

    // -------------------- ALL-EMPLOYEES SALARY (one month) --------------------
    // Salary page "All employees" view: har active employee ki us month ki net payable.
    [HttpGet("salary-all")]
    public async Task<ActionResult<SalaryAllDto>> SalaryAll([FromQuery] string month)
    {
        if (!TryParseMonth(month, out var first)) return BadRequest("month must be yyyy-MM.");

        var employees = await _db.Employees.Include(e => e.Shift)
            .Where(e => e.IsActive).OrderBy(e => e.Code).ToListAsync();
        var overtimePayable = (await _db.Settings.AsNoTracking().FirstOrDefaultAsync())?.OvertimePayable ?? false;

        var rows = new List<EmployeeSalaryRowDto>();
        foreach (var e in employees)
        {
            var shift = e.Shift ?? await _db.Shifts.FindAsync(e.ShiftId);
            if (shift is null) continue;
            var (_, s) = await ComputeMonthAsync(e, shift, first, overtimePayable);
            rows.Add(new EmployeeSalaryRowDto(
                e.Id, e.Code, e.Name, s.MonthlySalary,
                s.PresentDays, s.HalfDays, s.AbsentDays,
                s.PaidLeaves, s.UnpaidLeaves, s.PayableDays,
                s.TotalNetMinutes, s.NetPayable));
        }

        var total = rows.Sum(r => r.NetPayable);
        return Ok(new SalaryAllDto(first.ToString("yyyy-MM"), rows, total));
    }

    /// <summary>
    /// Compute a single employee's month: per-day grid + the salary summary. Shared by the
    /// per-employee Report and the all-employees salary list so the math stays identical.
    /// </summary>
    private async Task<(List<AttendanceDayDto> Days, MonthlySummaryDto Summary)> ComputeMonthAsync(
        Employee employee, Shift shift, DateTime first, bool overtimePayable)
    {
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);

        // Preload holidays & approved leaves for the month for paid/unpaid classification.
        var monthEnd = first.AddMonths(1).AddDays(-1);
        var holidays = await _db.Holidays.AsNoTracking()
            .Where(h => h.Date >= first && h.Date <= monthEnd).ToListAsync();
        var leaves = await _db.Leaves.AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id && l.Status == LeaveStatus.Approved &&
                        l.FromDate <= monthEnd && l.ToDate >= first).ToListAsync();

        var dayDtos = new List<AttendanceDayDto>();
        var computed = new List<(DateTime Date, AttendanceDay Day)>();
        DateTime? firstPresent = null, lastPresent = null;

        // Pass 1: compute every day's status, build the full-month grid, and find the
        // ACTIVE ATTENDANCE WINDOW = first present day .. last present day. We start
        // from the first day the employee was actually present (not the joining date —
        // some people start coming a few days after joining).
        for (var dayNum = 1; dayNum <= daysInMonth; dayNum++)
        {
            var date = new DateTime(first.Year, first.Month, dayNum);
            var day = await _svc.ComputeTransientAsync(employee, shift, date);
            dayDtos.Add(day.ToDto());
            computed.Add((date, day));
            if (day.Status is DayStatus.Present or DayStatus.HalfDay)
            {
                firstPresent ??= date;
                lastPresent = date;
            }
        }

        int presentDays = 0, halfDays = 0, absentDays = 0;
        int paidHolidays = 0, unpaidHolidays = 0, paidLeaves = 0, unpaidLeaves = 0, weeklyOffs = 0;
        int totalNet = 0;

        // A full working day = shift.RequiredMinutes (e.g. 480 = 8h). Salary is pro-rated by
        // the ACTUAL hours worked: a worked day pays (hours / required) of a full day.
        // So 3h on an 8h shift pays 3/8, 6h pays 6/8. Paid leave/holiday/weeklyOff = full day.
        // OVERTIME: if AppSetting.OvertimePayable is ON, a >8h day pays >1.0 (e.g. 10h = 10/8);
        // if OFF (default), each day is capped at a full day (overtime not paid extra).
        var requiredMinutes = shift.RequiredMinutes > 0 ? shift.RequiredMinutes : 480;
        double payableWorkDays = 0;

        double DayFraction(int netMinutes)
        {
            var fraction = netMinutes / (double)requiredMinutes;
            return overtimePayable ? fraction : Math.Min(fraction, 1.0);
        }

        // Pass 2: count ONLY days inside [firstPresent, lastPresent]. Sundays / paid
        // holidays before the first present day (not joined/started yet) or after the
        // last present day (future "advance" days) are NOT paid; future days aren't
        // counted as absent either.
        foreach (var (date, day) in computed)
        {
            if (firstPresent is null || date < firstPresent || date > lastPresent) continue;
            totalNet += day.NetMinutes;
            switch (day.Status)
            {
                case DayStatus.Present:
                    presentDays++;
                    payableWorkDays += DayFraction(day.NetMinutes);
                    break;
                case DayStatus.HalfDay:
                    halfDays++;
                    payableWorkDays += DayFraction(day.NetMinutes);
                    break;
                case DayStatus.Absent: absentDays++; break;
                case DayStatus.WeeklyOff: weeklyOffs++; break;
                case DayStatus.Holiday:
                    var hol = holidays.FirstOrDefault(h => h.Date == date);
                    if (hol is { IsPaid: false }) unpaidHolidays++; else paidHolidays++;
                    break;
                case DayStatus.Leave:
                    var lv = leaves.FirstOrDefault(l => l.FromDate <= date && l.ToDate >= date);
                    if (lv is { IsPaid: false }) unpaidLeaves++; else paidLeaves++;
                    break;
            }
        }

        var payableDays = payableWorkDays + paidHolidays + paidLeaves + weeklyOffs;

        // ----- Salary (hour-accurate) -----
        var monthlySalary = employee.MonthlySalary;
        var perDaySalary = daysInMonth > 0 ? monthlySalary / daysInMonth : 0m;          // e.g. 80000/30 = 2666.67
        var perHourSalary = perDaySalary / (requiredMinutes / 60m);                     // e.g. 2666.67/8 = 333.33
        var earnedSalary = Math.Round(perDaySalary * (decimal)payableDays, 0, MidpointRounding.AwayFromZero);

        // Loss of pay = absent/unpaid full days + the shortfall hours on worked-but-short days
        // (e.g. a 3h present day on an 8h shift loses 5h => 5/8 of a day's pay).
        var workedDayCount = presentDays + halfDays;
        // Shortfall only counts UNDER-time (clamp >=0 so overtime never shows as negative loss).
        var workShortfall = Math.Max(0, workedDayCount - payableWorkDays);
        var lostDays = absentDays + unpaidLeaves + unpaidHolidays + workShortfall;
        var lossOfPay = Math.Round(perDaySalary * (decimal)lostDays, 0, MidpointRounding.AwayFromZero);
        var netPayable = earnedSalary;

        var summary = new MonthlySummaryDto(
            presentDays, halfDays, absentDays, paidHolidays, unpaidHolidays,
            paidLeaves, unpaidLeaves, weeklyOffs, totalNet, payableDays,
            monthlySalary, daysInMonth, perDaySalary, earnedSalary, lossOfPay, netPayable,
            requiredMinutes, perHourSalary, payableWorkDays);

        return (dayDtos, summary);
    }

    // -------------------- MANUAL: list punches --------------------
    [HttpGet("punches")]
    public async Task<ActionResult<IEnumerable<PunchDto>>> GetPunches(
        [FromQuery] string date, [FromQuery] int employeeId)
    {
        if (!TryParseDate(date, out var d)) return BadRequest("date must be yyyy-MM-dd.");
        var startUtc = BusinessClock.ToUtc(d);
        var endUtc = BusinessClock.ToUtc(d.AddDays(1));
        var list = await _db.Punches.AsNoTracking().Include(p => p.Employee)
            .Where(p => p.EmployeeId == employeeId && p.Timestamp >= startUtc && p.Timestamp < endUtc)
            .OrderBy(p => p.Timestamp).ToListAsync();
        return Ok(list.Select(p => p.ToDto()));
    }

    // -------------------- MANUAL: create punch --------------------
    [HttpPost("punches")]
    public async Task<ActionResult<PunchDto>> CreatePunch(ManualPunchCreateDto dto)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
        if (employee is null) return BadRequest($"Employee {dto.EmployeeId} does not exist.");

        var punch = new AttendancePunch
        {
            EmployeeId = dto.EmployeeId,
            Timestamp = BusinessClock.ToUtc(dto.Timestamp), // admin-typed time is IST -> store UTC
            Direction = dto.Direction,
            Source = PunchSource.Manual,
            Note = dto.Note
        };
        _db.Punches.Add(punch);
        await _db.SaveChangesAsync();

        await _svc.RecomputeDayAsync(dto.EmployeeId, dto.Timestamp.Date);

        punch.Employee = employee;
        return CreatedAtAction(nameof(GetPunches),
            new { date = dto.Timestamp.ToString("yyyy-MM-dd"), employeeId = dto.EmployeeId },
            punch.ToDto());
    }

    // -------------------- MANUAL: edit punch --------------------
    [HttpPut("punches/{id:int}")]
    public async Task<ActionResult<PunchDto>> UpdatePunch(int id, ManualPunchUpdateDto dto)
    {
        var punch = await _db.Punches.Include(p => p.Employee).FirstOrDefaultAsync(p => p.Id == id);
        if (punch is null) return NotFound();

        var oldIstDate = BusinessClock.ToLocal(punch.Timestamp).Date;
        punch.Timestamp = BusinessClock.ToUtc(dto.Timestamp); // admin-typed time is IST -> store UTC
        punch.Direction = dto.Direction;
        if (dto.Note != null) punch.Note = dto.Note;
        punch.Source = PunchSource.Manual;
        await _db.SaveChangesAsync();

        var newIstDate = dto.Timestamp.Date;
        await _svc.RecomputeDayAsync(punch.EmployeeId, newIstDate);
        if (oldIstDate != newIstDate)
            await _svc.RecomputeDayAsync(punch.EmployeeId, oldIstDate);

        return Ok(punch.ToDto());
    }

    // -------------------- MANUAL: delete punch --------------------
    [HttpDelete("punches/{id:int}")]
    public async Task<IActionResult> DeletePunch(int id)
    {
        var punch = await _db.Punches.FirstOrDefaultAsync(p => p.Id == id);
        if (punch is null) return NotFound();
        var empId = punch.EmployeeId;
        var date = BusinessClock.ToLocal(punch.Timestamp).Date; // IST day to recompute
        _db.Punches.Remove(punch);
        await _db.SaveChangesAsync();
        await _svc.RecomputeDayAsync(empId, date);
        return NoContent();
    }

    // -------------------- MANUAL: day override --------------------
    [HttpPut("day")]
    public async Task<ActionResult<AttendanceDayDto>> OverrideDay(DayOverrideDto dto)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
        if (employee is null) return BadRequest($"Employee {dto.EmployeeId} does not exist.");

        var date = dto.Date.Date;
        var day = await _db.Days.FirstOrDefaultAsync(d => d.EmployeeId == dto.EmployeeId && d.Date == date);
        if (day is null)
        {
            day = new AttendanceDay { EmployeeId = dto.EmployeeId, Date = date };
            _db.Days.Add(day);
        }

        day.IsManual = true;
        day.ManualNote = dto.ManualNote;
        if (dto.NetMinutes.HasValue) day.NetMinutes = dto.NetMinutes.Value;
        if (dto.Status.HasValue) day.Status = dto.Status.Value;

        await _db.SaveChangesAsync();
        day.Employee = employee;
        return Ok(day.ToDto());
    }

    // -------------------- helpers --------------------
    private static bool TryParseDate(string s, out DateTime date) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private static bool TryParseMonth(string s, out DateTime first)
    {
        first = default;
        if (DateTime.TryParseExact(s + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d))
        {
            first = d;
            return true;
        }
        return false;
    }
}
