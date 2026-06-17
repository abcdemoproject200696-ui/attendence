using Attendance.Api.Dtos;
using Attendance.Domain.Entities;

namespace Attendance.Api;

public static class Mapping
{
    private const string DateFmt = "yyyy-MM-dd";

    public static ShiftDto ToDto(this Shift s) => new(
        s.Id, s.Name, s.ShiftStart, s.ShiftEnd, s.RequiredMinutes,
        s.LunchStart, s.LunchEnd, s.AutoDeductLunch, s.LunchPaid,
        s.GraceMinutes, s.HalfDayThresholdMinutes, s.WeeklyOffDays.ToList());

    public static RoleDto ToDto(this Role r) => new(r.Id, r.Name, r.IsActive);

    public static PageDto ToDto(this Page p) => new(p.Id, p.Key, p.Name, p.Route, p.MenuOrder);

    public static EmployeeDto ToDto(this Employee e) => new(
        e.Id, e.Code, e.Name, e.RoleId, e.Role?.Name ?? string.Empty, e.Email, e.Phone,
        e.ShiftId, e.MonthlySalary, e.IsActive, e.PhotoUrl, e.HasFace, e.FaceCount, e.CreatedAt);

    public static PunchDto ToDto(this AttendancePunch p) => new(
        p.Id, p.EmployeeId, p.Employee?.Code, p.Employee?.Name,
        BusinessClock.ToLocal(p.Timestamp), // stored UTC -> IST for display
        p.Direction.ToString(), p.DeviceId, p.Source.ToString(), p.Note);

    public static AttendanceDayDto ToDto(this AttendanceDay d) => new(
        d.Id, d.EmployeeId, d.Employee?.Name, d.Date.ToString(DateFmt),
        d.FirstIn, d.LastOut, d.GrossMinutes, d.BreakMinutes, d.LunchDeduction,
        d.LunchFrom, d.LunchTo,
        d.NetMinutes, d.Status.ToString(), d.HasOpenSession, d.IsManual, d.ManualNote);

    public static HolidayDto ToDto(this Holiday h) => new(
        h.Id, h.Date.ToString(DateFmt), h.Name, h.IsPaid);

    public static AppSettingDto ToDto(this AppSetting s) => new(
        s.Id, s.FaceMatchThreshold, s.RequireLiveness, s.VoiceEnabled, s.OvertimePayable);

    public static LeaveDto ToDto(this LeaveRequest l) => new(
        l.Id, l.EmployeeId, l.Employee?.Name, l.FromDate.ToString(DateFmt),
        l.ToDate.ToString(DateFmt), l.Type.ToString(), l.IsPaid, l.Status.ToString(), l.Reason);
}
