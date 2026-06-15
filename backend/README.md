# Attendance System — Backend (.NET 10 Web API)

Face-Scanner Attendance System backend. Layered clean architecture, EF Core + SQLite.
Implements `CONTRACT.md` (single source of truth) exactly: models, endpoints, enums,
and the pure `AttendanceCalculator`.

## Projects

| Project | Type | Purpose |
|---|---|---|
| `Attendance.Domain` | classlib | Entities, enums, and the **pure** `AttendanceCalculator` (no EF/DB). Testable core. |
| `Attendance.Infrastructure` | classlib | `AppDbContext` (EF Core SQLite), entity configs, `DbSeeder`. |
| `Attendance.Api` | web api | Controllers, DTOs, mapping, `Program.cs` (EF, EnsureCreated + seed, Swagger, CORS). |
| `Attendance.Tests` | xUnit | Unit tests for `AttendanceCalculator`. |

## How to run

```bash
cd d:\TestProject\attendance\backend
dotnet run --project Attendance.Api
```

On startup the DB (`attendance.db` SQLite) is created via `EnsureCreated` and seeded with
1 default shift, 3 employees, and a few holidays — so the API returns data immediately.

- **Base URL:** `http://localhost:5080/api`
- **Swagger UI:** `http://localhost:5080/swagger`

CORS allows any origin in dev (covers `http://localhost:8100`, `:4200`, `:80`, and
Capacitor/Ionic origins).

## Build & test

```bash
dotnet build Attendance.sln      # 0 warnings, 0 errors
dotnet test  Attendance.sln      # 16 tests pass
```

> Note: a local `nuget.config` restricts package restore to `nuget.org` (the machine's
> secondary feed `nuget.fast-report.com` was returning HTTP 403).

## Endpoints

### Employees
- `GET    /api/employees`
- `GET    /api/employees/{id}`
- `POST   /api/employees`
- `PUT    /api/employees/{id}`
- `DELETE /api/employees/{id}`

### Shifts
- `GET  /api/shifts`
- `GET  /api/shifts/{id}`
- `POST /api/shifts`
- `PUT  /api/shifts/{id}`

### Attendance — punch & view
- `POST /api/attendance/punch` — resolve employee by `employeeId` / `employeeCode` /
  `faceDescriptor` (Euclidean distance < 0.5), decide IN/OUT from last punch of the day,
  save + recompute. Returns `{ punch, todayNetMinutes, message }`.
- `GET  /api/attendance/today/{employeeId}`
- `GET  /api/attendance/daily?date=YYYY-MM-DD`
- `GET  /api/attendance/report?month=YYYY-MM&employeeId={id}`
- `POST /api/attendance/recompute?date=YYYY-MM-DD&employeeId={id}`

### Attendance — manual correction (admin)
- `GET    /api/attendance/punches?date=YYYY-MM-DD&employeeId={id}`
- `POST   /api/attendance/punches` (recompute)
- `PUT    /api/attendance/punches/{id}` (recompute)
- `DELETE /api/attendance/punches/{id}` (recompute)
- `PUT    /api/attendance/day` — manual override (`isManual=true`), calc skipped.

### Holidays
- `GET    /api/holidays?year=2026`
- `POST   /api/holidays`
- `PUT    /api/holidays/{id}`
- `DELETE /api/holidays/{id}`

### Leaves
- `GET    /api/leaves?employeeId={id}&month=YYYY-MM`
- `POST   /api/leaves` (status=Pending)
- `PUT    /api/leaves/{id}` (approving an approved/leave recomputes affected days)
- `DELETE /api/leaves/{id}`

## AttendanceCalculator (core logic)

Pure, DB-free. Per `CONTRACT.md`:
1. Sort punches; debounce same-direction punches < 60s apart.
2. Pair IN→OUT sessions; `grossMinutes = Σ(out-in)`.
3. Trailing IN with no OUT → `hasOpenSession=true` (excluded from gross).
4. `breakMinutes = (lastOut - firstIn) - grossMinutes`.
5. `lunchDeduction = Σ overlap(session, [lunchStart, lunchEnd])` when `autoDeductLunch`
   (no double-deduct if already punched out over lunch).
6. `netMinutes = grossMinutes - lunchDeduction`.
7. Status precedence: Holiday > WeeklyOff > Leave > (net ≥ required → Present;
   ≥ halfDayThreshold → HalfDay; else Absent).
8. `isManual=true` → stored override values used, calc skipped.

Monthly summary: `payableDays = presentDays + 0.5*halfDays + paidHolidays + paidLeaves + weeklyOffs`.
