// TypeScript models — EXACTLY matching CONTRACT.md (JSON camelCase).
// No `any`. These are the single source of truth on the frontend.

// ===== Enum string-union types =====
export type Direction = 'IN' | 'OUT';
export type PunchSource = 'Face' | 'Code' | 'Manual';
export type DayStatus = 'Present' | 'HalfDay' | 'Absent' | 'Holiday' | 'Leave' | 'WeeklyOff';
export type LeaveType = 'Casual' | 'Sick' | 'Paid' | 'Unpaid';
export type LeaveStatus = 'Pending' | 'Approved' | 'Rejected';

// ===== Shift =====
export interface Shift {
  id: number;
  name: string;
  shiftStart: string; // "10:00"
  shiftEnd: string; // "19:00"
  requiredMinutes: number;
  lunchStart: string; // "13:00"
  lunchEnd: string; // "14:00"
  autoDeductLunch: boolean;
  lunchPaid: boolean;
  graceMinutes: number;
  halfDayThresholdMinutes: number;
  weeklyOffDays: number[]; // 0=Sunday..6=Saturday
}

export type ShiftInput = Omit<Shift, 'id'>;

// ===== Role (software-company designations; seeded) =====
export interface Role {
  id: number;
  name: string;
  isActive: boolean; // admin Settings toggle — only active roles show in Add Employee dropdown
}

// ===== RBAC =====
// A page/menu item the app exposes; pages are seeded on the backend.
export interface Page {
  id: number;
  key: string; // "dashboard", "kiosk", ...
  name: string;
  route: string; // "/dashboard"
  menuOrder: number;
}

// POST /auth/login response (basic client-side auth — see CONTRACT.md RBAC).
export interface LoginResult {
  employeeId: number;
  code: string;
  name: string;
  roleId: number;
  roleName: string;
  allowedPages: string[]; // page keys this user may open
}

// GET/PUT /roles/{roleId}/permissions
export interface RolePermissions {
  roleId: number;
  pageIds: number[];
}

// ===== Employee =====
export interface Employee {
  id: number;
  code: string; // "EMP001"
  name: string;
  roleId: number;
  roleName: string;
  email: string;
  phone: string;
  shiftId: number;
  monthlySalary: number;
  isActive: boolean;
  photoUrl?: string | null;
  hasFace: boolean;
  faceCount: number;
  createdAt: string;
}

// Body for POST/PUT /employees. faceDescriptors optional (1..5 of number[128]).
// code optional — for NEW employees leave empty so backend auto-generates "EMP00X".
export interface EmployeeInput {
  code?: string;
  name: string;
  roleId: number;
  email: string;
  phone: string;
  shiftId: number;
  monthlySalary: number;
  isActive?: boolean;
  faceDescriptors?: number[][];
  // Login password. For NEW employees sets it; for EDIT leave blank to keep the old one.
  password?: string;
}

// ===== AttendancePunch =====
export interface AttendancePunch {
  id: number;
  employeeId: number;
  employeeCode?: string | null;
  employeeName: string;
  timestamp: string; // ISO-8601
  direction: Direction;
  deviceId?: string | null;
  source: PunchSource;
  note?: string | null;
}

// ===== AttendanceDay (calculated; manual override possible) =====
export interface AttendanceDay {
  id: number;
  employeeId: number;
  employeeName: string;
  date: string; // "yyyy-MM-dd"
  firstIn?: string | null;
  lastOut?: string | null;
  grossMinutes: number;
  breakMinutes: number;
  lunchDeduction: number;
  lunchFrom?: string | null; // ISO timestamp — start of auto-deducted lunch window
  lunchTo?: string | null; // ISO timestamp — end of auto-deducted lunch window
  netMinutes: number;
  status: DayStatus;
  hasOpenSession: boolean;
  isManual: boolean;
  manualNote?: string | null;
}

// ===== Holiday =====
export interface Holiday {
  id: number;
  date: string; // "2026-11-08"
  name: string;
  isPaid: boolean;
}

export type HolidayInput = Omit<Holiday, 'id'>;

// ===== LeaveRequest =====
export interface LeaveRequest {
  id: number;
  employeeId: number;
  employeeName: string;
  fromDate: string; // "2026-06-20"
  toDate: string; // "2026-06-21"
  type: LeaveType;
  isPaid: boolean;
  status: LeaveStatus;
  reason?: string | null;
}

export interface LeaveInput {
  employeeId: number;
  fromDate: string;
  toDate: string;
  type: LeaveType;
  isPaid: boolean;
  reason?: string;
}

// PUT /leaves/{id} — any subset of these.
export interface LeaveUpdate {
  status?: LeaveStatus;
  fromDate?: string;
  toDate?: string;
  type?: LeaveType;
  isPaid?: boolean;
  reason?: string;
}

// ===== MonthlyReport =====
export interface MonthlySummary {
  presentDays: number;
  halfDays: number;
  absentDays: number;
  paidHolidays: number;
  unpaidHolidays: number;
  paidLeaves: number;
  unpaidLeaves: number;
  weeklyOffs: number;
  totalNetMinutes: number;
  payableDays: number;
  // ===== Salary (admin) =====
  monthlySalary: number;
  totalDaysInMonth: number;
  perDaySalary: number;
  earnedSalary: number;
  lossOfPay: number;
  netPayable: number;
  // ===== Hour-based salary detail =====
  requiredMinutesPerDay: number; // a full working day, e.g. 480 = 8h
  perHourSalary: number; // perDaySalary / (requiredMinutesPerDay/60)
  payableWorkDays: number; // sum of hour-fractions of worked days (caps each day at 1.0)
}

export interface MonthlyReport {
  employeeId: number;
  employeeName: string;
  month: string; // "2026-06"
  days: AttendanceDay[];
  summary: MonthlySummary;
}

// ===== All-employees salary (one month) =====
export interface EmployeeSalaryRow {
  employeeId: number;
  code: string;
  name: string;
  monthlySalary: number;
  presentDays: number;
  halfDays: number;
  absentDays: number;
  paidLeaves: number;
  unpaidLeaves: number;
  payableDays: number;
  totalNetMinutes: number;
  netPayable: number;
}

export interface SalaryAll {
  month: string; // "2026-06"
  rows: EmployeeSalaryRow[];
  totalNetPayable: number;
}

// ===== Request DTOs (attendance) =====
export interface PunchRequest {
  employeeId?: number;
  employeeCode?: string;
  faceDescriptor?: number[];
  deviceId: string;
  source: PunchSource;
}

export interface PunchResult {
  punch: AttendancePunch;
  todayNetMinutes: number;
  message: string;
  // Only present on a FACE match: how close the descriptor matched.
  matchDistance?: number; // Euclidean distance (lower = closer)
  matchConfidence?: number; // 0..100 (%) — derived from distance vs threshold
}

// ===== AppSetting (single-row global settings; admin-editable) =====
export interface AppSetting {
  id: number;
  faceMatchThreshold: number; // 0.3..0.7 — face match max Euclidean distance (lower = stricter)
  requireLiveness: boolean; // kiosk requires a blink before punching (anti photo-spoof)
  voiceEnabled: boolean; // kiosk speaks a greeting aloud on punch
  overtimePayable: boolean; // pay for overtime hours in salary (off = each day capped at full day)
  hrCanEditAttendance: boolean; // admin grants HR (roleId 2) permission to manually edit attendance
}

export interface PunchInput {
  employeeId: number;
  timestamp: string;
  direction: Direction;
  note?: string;
}

export interface PunchEdit {
  timestamp: string;
  direction: Direction;
  note?: string;
}

export interface DayOverride {
  employeeId: number;
  date: string;
  netMinutes?: number;
  status?: DayStatus;
  manualNote?: string;
}
