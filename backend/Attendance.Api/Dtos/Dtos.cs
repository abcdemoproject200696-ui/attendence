using System.ComponentModel.DataAnnotations;
using Attendance.Domain;

namespace Attendance.Api.Dtos;

// ---------- Shift ----------
public record ShiftDto(
    int Id, string Name, string ShiftStart, string ShiftEnd, int RequiredMinutes,
    string LunchStart, string LunchEnd, bool AutoDeductLunch, bool LunchPaid,
    int GraceMinutes, int HalfDayThresholdMinutes, List<int> WeeklyOffDays);

public class ShiftInputDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string ShiftStart { get; set; } = "10:00";
    [Required] public string ShiftEnd { get; set; } = "19:00";
    [Range(0, 1440)] public int RequiredMinutes { get; set; } = 480;
    [Required] public string LunchStart { get; set; } = "13:00";
    [Required] public string LunchEnd { get; set; } = "14:00";
    public bool AutoDeductLunch { get; set; } = true;
    public bool LunchPaid { get; set; }
    [Range(0, 120)] public int GraceMinutes { get; set; } = 5;
    [Range(0, 1440)] public int HalfDayThresholdMinutes { get; set; } = 240;
    public List<int> WeeklyOffDays { get; set; } = new() { 0 };
}

// ---------- Role ----------
public record RoleDto(int Id, string Name, bool IsActive);

public class RoleInputDto
{
    // Name required on create (checked in controller); optional on update (rename only if provided).
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}

// ---------- Employee ----------
public record EmployeeDto(
    int Id, string Code, string Name, string? FirstName, string? LastName, int RoleId, string RoleName, string? Email, string? Phone,
    int ShiftId, decimal MonthlySalary, bool IsActive, string? PhotoUrl, string? Gender, string? BloodGroup, string? Dob, bool HasFace, int FaceCount, DateTime CreatedAt);

public class EmployeeInputDto
{
    // Optional: empty/null => backend auto-generates next "EMP00X".
    public string? Code { get; set; }
    // Name is derived as "First Last" when First/Last are sent; either form works.
    public string Name { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    [Required] public int RoleId { get; set; }
    [EmailAddress] public string? Email { get; set; }
    public string? Phone { get; set; }
    [Required] public int ShiftId { get; set; }
    [Range(0, double.MaxValue)] public decimal MonthlySalary { get; set; }
    public bool IsActive { get; set; } = true;
    public string? PhotoUrl { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }
    public string? Dob { get; set; }
    /// <summary>1..5 face descriptors (each 128-d). When provided, replaces the employee's enrolled faces.</summary>
    public List<List<double>>? FaceDescriptors { get; set; }
    /// <summary>Login password. When non-empty, sets/resets the hash; empty/null on update keeps existing.</summary>
    public string? Password { get; set; }
}

// ---------- Page / RBAC ----------
public record PageDto(int Id, string Key, string Name, string Route, int MenuOrder);

public record RolePermissionsDto(int RoleId, List<int> PageIds);

public class RolePermissionsInputDto
{
    public List<int> PageIds { get; set; } = new();
}

// ---------- Auth ----------
public class LoginRequestDto
{
    [Required] public string Code { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
}

public record LoginResultDto(
    int EmployeeId, string Code, string Name, int RoleId, string RoleName, List<string> AllowedPages,
    string? PhotoUrl = null);

// ---------- Forgot / reset password (OTP) ----------
public class ForgotOtpRequestDto
{
    public string? Code { get; set; }
}

public class ResetPasswordRequestDto
{
    public string? Code { get; set; }
    public string? Otp { get; set; }
    public string? Password { get; set; }
}

public record OtpResponseDto(string Otp);

// ---------- Punch ----------
public record PunchDto(
    int Id, int EmployeeId, string? EmployeeCode, string? EmployeeName, DateTime Timestamp, string Direction,
    string? DeviceId, string Source, string? Note);

public class PunchRequestDto
{
    public int? EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public List<double>? FaceDescriptor { get; set; }
    public string? DeviceId { get; set; }
    public PunchSource Source { get; set; } = PunchSource.Code;
}

public record PunchResultDto(
    PunchDto Punch, int TodayNetMinutes, string Message,
    double? MatchDistance = null, int? MatchConfidence = null);

public class ManualPunchCreateDto
{
    [Required] public int EmployeeId { get; set; }
    [Required] public DateTime Timestamp { get; set; }
    [Required] public Direction Direction { get; set; }
    public string? Note { get; set; }
}

public class ManualPunchUpdateDto
{
    [Required] public DateTime Timestamp { get; set; }
    [Required] public Direction Direction { get; set; }
    public string? Note { get; set; }
}

// ---------- AttendanceDay ----------
public record AttendanceDayDto(
    int Id, int EmployeeId, string? EmployeeName, string Date,
    DateTime? FirstIn, DateTime? LastOut, int GrossMinutes, int BreakMinutes,
    int LunchDeduction, DateTime? LunchFrom, DateTime? LunchTo,
    int NetMinutes, string Status, bool HasOpenSession,
    bool IsManual, string? ManualNote);

public class DayOverrideDto
{
    [Required] public int EmployeeId { get; set; }
    [Required] public DateTime Date { get; set; }
    public int? NetMinutes { get; set; }
    public DayStatus? Status { get; set; }
    public string? ManualNote { get; set; }
}

// ---------- Holiday ----------
public record HolidayDto(int Id, string Date, string Name, bool IsPaid);

public class HolidayInputDto
{
    [Required] public DateTime Date { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; } = true;
}

// ---------- Leave ----------
public record LeaveDto(
    int Id, int EmployeeId, string? EmployeeName, string FromDate, string ToDate,
    string Type, bool IsPaid, string Status, string? Reason);

public class LeaveInputDto
{
    [Required] public int EmployeeId { get; set; }
    [Required] public DateTime FromDate { get; set; }
    [Required] public DateTime ToDate { get; set; }
    [Required] public LeaveType Type { get; set; }
    public bool IsPaid { get; set; } = true;
    public string? Reason { get; set; }
}

public class LeaveUpdateDto
{
    public LeaveStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public LeaveType? Type { get; set; }
    public bool? IsPaid { get; set; }
    public string? Reason { get; set; }
}

// ---------- App settings ----------
public record AppSettingDto(
    int Id, double FaceMatchThreshold, bool RequireLiveness, bool VoiceEnabled, bool OvertimePayable,
    bool HrCanEditAttendance);

public class AppSettingUpdateDto
{
    public double? FaceMatchThreshold { get; set; }
    public bool? RequireLiveness { get; set; }
    public bool? VoiceEnabled { get; set; }
    public bool? OvertimePayable { get; set; }
    public bool? HrCanEditAttendance { get; set; }
}

// ---------- Monthly report ----------
public record MonthlySummaryDto(
    int PresentDays, int HalfDays, int AbsentDays, int PaidHolidays, int UnpaidHolidays,
    int PaidLeaves, int UnpaidLeaves, int WeeklyOffs, int TotalNetMinutes, double PayableDays,
    // ----- SALARY (admin) -----
    decimal MonthlySalary, int TotalDaysInMonth, decimal PerDaySalary,
    decimal EarnedSalary, decimal LossOfPay, decimal NetPayable,
    // ----- HOUR-BASED salary detail -----
    int RequiredMinutesPerDay, decimal PerHourSalary, double PayableWorkDays);

public record MonthlyReportDto(
    int EmployeeId, string? EmployeeName, string Month,
    List<AttendanceDayDto> Days, MonthlySummaryDto Summary);

// ---------- All-employees salary (one month, every active employee) ----------
public record EmployeeSalaryRowDto(
    int EmployeeId, string Code, string Name, decimal MonthlySalary,
    int PresentDays, int HalfDays, int AbsentDays,
    int PaidLeaves, int UnpaidLeaves, double PayableDays,
    int TotalNetMinutes, decimal NetPayable);

public record SalaryAllDto(string Month, List<EmployeeSalaryRowDto> Rows, decimal TotalNetPayable);

// ---------- Task (Kanban) ----------
public record TaskDto(
    int Id, string Title, string? Description, int AssigneeId, string AssigneeName,
    int AssignedById, string AssignedByName, string Status, string Priority,
    string? DueDate, int? ProjectId, string? ProjectName, string? StartTime, string? EndTime,
    int AttachmentCount, DateTime CreatedAt);

public class TaskInputDto
{
    [Required] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Required] public int AssigneeId { get; set; }
    public int? AssignedById { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public string? DueDate { get; set; }
    public int? ProjectId { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}

public class TaskStatusInputDto
{
    [Required] public string Status { get; set; } = string.Empty;
}

// ---------- Employee (brief) ----------
public record EmployeeBriefDto(int Id, string Name, string Code);

// ---------- Project ----------
public record ProjectDto(
    int Id, string Name, string? Description, string Status, int CreatedById, string? CreatedByName,
    DateTime CreatedAt, int TaskCount, int EmployeeCount);

public record ProjectDetailDto(
    int Id, string Name, string? Description, string Status, int CreatedById, string? CreatedByName,
    DateTime CreatedAt, int TaskCount, int EmployeeCount,
    List<TaskDto> Tasks, List<EmployeeBriefDto> Employees);

public class ProjectInputDto
{
    [Required] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Status { get; set; } // Active | Hold | Inactive
    public int? CreatedById { get; set; }
}

// ---------- Task attachment ----------
public record AttachmentDto(
    int Id, int TaskId, string FileName, string MimeType, DateTime CreatedAt);

public record AttachmentDataDto(
    int Id, int TaskId, string FileName, string MimeType, string DataBase64, DateTime CreatedAt);

public class AttachmentInputDto
{
    [Required] public string FileName { get; set; } = string.Empty;
    [Required] public string MimeType { get; set; } = string.Empty;
    [Required] public string DataBase64 { get; set; } = string.Empty;
}

public record TaskCommentDto(
    int Id, int TaskId, int AuthorId, string AuthorName, string Body, DateTime CreatedAt, int? ParentId);

public class TaskCommentInputDto
{
    public int AuthorId { get; set; }
    [Required] public string Body { get; set; } = string.Empty;
    // When set, this comment is a reply to comment ParentId (threaded).
    public int? ParentId { get; set; }
}
