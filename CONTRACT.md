# Attendance System — API Contract & Rules (SINGLE SOURCE OF TRUTH)

> Frontend aur Backend **dono** is file ko follow karenge. Koi bhi field/endpoint yahan se badle to dono jagah update hona chahiye. JSON me **camelCase** use hoga (backend me `System.Text.Json` default camelCase).

## Tech Stack (STRICT)
- **Backend:** .NET 10 Web API, EF Core + **SQLite** (`attendance.db`), layered: `Attendance.Domain`, `Attendance.Infrastructure`, `Attendance.Api`, `Attendance.Tests` (xUnit). Swagger ON. CORS allow frontend.
- **Frontend:** Ionic 8 + Angular 20 **standalone components**, Capacitor 8 (mobile app). Responsive (mobile + tablet + PC browser). HttpClient services. `environment.apiUrl`.
- JSON camelCase. Dates: `date` = `"yyyy-MM-dd"`, timestamps ISO-8601. Times of day = `"HH:mm"` (24h).

## Backend base URL
`http://localhost:5080/api`  (frontend `environment.apiUrl = 'http://localhost:5080/api'`)

---

## RBAC — Role-Based Access Control (login + page permissions)

### Page (seeded; har page/menu ka ek id + key)
```
{ id, key, name, route, menuOrder }
```
Seed (id, key, name, route):
```
1  dashboard    Dashboard          /dashboard
2  kiosk        Kiosk              /kiosk
3  employees    Employees          /employees
4  daily        Daily Attendance   /daily
5  report       Monthly Report     /report
6  holidays     Holidays           /holidays
7  leaves       Leaves             /leaves
8  salary       Salary             /salary
9  settings     Settings           /settings
10 permissions  Permissions        /permissions
```

### Employee — add password (login ke liye)
- Employee entity me `PasswordHash` (SHA-256 hex, nullable). DTO me kabhi mat bhejo.
- `EmployeeInput` me optional `password` — diya gaya to set/reset (hash karke store). Khaali chhoda to purana rahe.
- Seed: kam se kam ek **Admin-role (roleId 1) employee** known creds ke saath. Documented:
  `EMP001 / admin123` (roleId 1 Admin). Baaki seeded employees ko bhi default password `pass123`.

### RolePagePermission (kaunsa role kaunse page dekh sakta hai)
- Join: `{ id, roleId, pageId }`. Admin (roleId 1) hamesha SAARE pages (chahe rows ho ya na ho).
- Default seed: Admin→all; HR(2)→ dashboard,kiosk,employees,daily,report,holidays,leaves,salary;
  baaki roles→ dashboard,kiosk.

### Auth + permission endpoints
- `POST /auth/login` { code, password }
    → 200 `{ employeeId, code, name, roleId, roleName, allowedPages: ["dashboard","kiosk",...] }`
      (Admin role → allowedPages = saare page keys.) Galat creds → 401.
- `GET /pages` → Page[]
- `GET /roles/{roleId}/permissions` → `{ roleId, pageIds:[...] }`  (Admin → saare pageIds)
- `PUT /roles/{roleId}/permissions` body `{ pageIds:[...] }` → permissions replace karo, updated return.
> Note: ye **basic auth** hai (login creds verify; client-side guard/menu). JWT/token nahi — internal/demo
> level (jaise admin PIN already client-side hai). Baad me JWT me upgrade kar sakte hain.

---

## Enums
- `Direction`: `"IN"` | `"OUT"`
- `PunchSource`: `"Face"` | `"Code"` | `"Manual"`
- `DayStatus`: `"Present"` | `"HalfDay"` | `"Absent"` | `"Holiday"` | `"Leave"` | `"WeeklyOff"`
- `LeaveType`: `"Casual"` | `"Sick"` | `"Paid"` | `"Unpaid"`
- `LeaveStatus`: `"Pending"` | `"Approved"` | `"Rejected"`

## Models (JSON shape)

### Shift
```
{ id, name, shiftStart:"10:00", shiftEnd:"19:00", requiredMinutes:480,
  lunchStart:"13:00", lunchEnd:"14:00", autoDeductLunch:true, lunchPaid:false,
  graceMinutes:5, halfDayThresholdMinutes:240, weeklyOffDays:[0] }  // 0=Sunday..6=Saturday
```

### Role  (software-company designations; seeded)
```
{ id, name, isActive }
// isActive: admin Settings me toggle. Sirf isActive=true roles "Add Employee" dropdown me dikhte hain.
//   (Edit par employee ka current role inactive ho to bhi dropdown me dikhao, taaki lost na ho.)
//   Seed: sab roles isActive=true. PUT /roles/{id} body {name?, isActive?} dono update kar sake.
// Seed: 1 Admin, 2 HR, 3 Supervisor, 4 Project Manager, 5 Team Lead,
//       6 Software Engineer, 7 Web Developer, 8 QA Engineer, 9 UI/UX Designer,
//       10 DevOps Engineer, 11 Staff
```
Endpoints: `GET /roles` → Role[] ; `POST /roles` {name} ; `PUT /roles/{id}` {name} ; `DELETE /roles/{id}` (in-use ho to 400).
Employee add/edit me role **dropdown** GET /roles se bharo.

### Employee
```
{ id, code:"EMP001", name, roleId:6, roleName:"Software Engineer", email, phone, shiftId,
  monthlySalary:55000, isActive:true, photoUrl, hasFace:false, faceCount:0, createdAt }
// 'role' free-text HATA diya — ab roleId (FK -> Role). roleName response me join se aata hai.
// EmployeeInput: roleId (number) bheja jaye (role string nahi).
// MULTI-PHOTO: ek employee ke 1..5 face descriptors (number[128] har ek) DB me store hote
//   hain (faceDescriptors: number[][]). list/GET response me descriptors NAHI bhejte —
//   sirf hasFace (count>0) aur faceCount (kitne enrolled) bhejte hain.
// CODE AUTO-GENERATE: POST par client code na bheje (ya khaali) to backend agla
//   "EMP00X" khud banaye (max existing code +1, zero-padded 3 digit). Client me read-only.
```
**EmployeeInput** (create/update): `faceDescriptors?: number[][]` (1..5 descriptors). Diya gaya to
employee ke faces replace ho. Backend matching me employee ke SAARE descriptors me se min distance
liya jaye (koi bhi ek match kare to bas). Punch request abhi bhi single `faceDescriptor` (ek live face).

### AttendancePunch
```
{ id, employeeId, employeeName, timestamp:"2026-06-15T10:00:00", direction:"IN",
  deviceId, source:"Code", note }
```

### AttendanceDay  (calculated; manual override possible)
```
{ id, employeeId, employeeName, date:"2026-06-15",
  firstIn, lastOut, grossMinutes, breakMinutes, lunchDeduction, netMinutes,
  status:"Present", hasOpenSession:false, isManual:false, manualNote }
```

### Holiday
```
{ id, date:"2026-11-08", name:"Diwali", isPaid:true }
```

### AppSetting (single-row global settings; admin-editable)
```
{ id, faceMatchThreshold:0.5, requireLiveness:false }
// faceMatchThreshold: face match max Euclidean distance (lower = stricter). Range 0.3..0.7.
// requireLiveness: kiosk me punch se pehle blink (liveness) zaroori — anti photo-spoof. Client-side enforce.
```

### LeaveRequest
```
{ id, employeeId, employeeName, fromDate:"2026-06-20", toDate:"2026-06-21",
  type:"Casual", isPaid:true, status:"Pending", reason }
```

### MonthlyReport (GET report response)
```
{ employeeId, employeeName, month:"2026-06",
  days: AttendanceDay[],   // har calendar day
  summary: { presentDays, halfDays, absentDays, paidHolidays, unpaidHolidays,
             paidLeaves, unpaidLeaves, weeklyOffs, totalNetMinutes, payableDays,
             // ----- SALARY (admin) -----
             monthlySalary, totalDaysInMonth, perDaySalary,
             earnedSalary, lossOfPay, netPayable } }
```
**Salary formula:** `perDaySalary = monthlySalary / totalDaysInMonth`;
`earnedSalary = round(perDaySalary * payableDays)`;
`lossOfPay = round(perDaySalary * (absentDays + unpaidLeaves + unpaidHolidays))`;
`netPayable = earnedSalary`. (payableDays = presentDays + 0.5*halfDays + paidHolidays + paidLeaves + weeklyOffs)

---

## Endpoints (REST, all under /api)

### Employees
- `GET    /employees`            → Employee[]
- `GET    /employees/{id}`       → Employee
- `POST   /employees`            body: {code,name,role,email,phone,shiftId,faceDescriptor?} → Employee
- `PUT    /employees/{id}`       → Employee
- `DELETE /employees/{id}`       → 204

### Shifts
- `GET  /shifts`        → Shift[]
- `GET  /shifts/{id}`   → Shift
- `POST /shifts`        → Shift
- `PUT  /shifts/{id}`   → Shift

### Attendance — punch & view
- `POST /attendance/punch`  body: {employeeId?|employeeCode?|faceDescriptor?, deviceId, source}
      → backend match kare (face/code), last punch dekh kar IN/OUT decide kare, punch save kare
      → returns { punch:AttendancePunch, todayNetMinutes, message, matchDistance?, matchConfidence? }
      → matchDistance/matchConfidence sirf face-match par bhejo. confidence = 0..100 (%),
        = round( max(0, (threshold - distance) / threshold) * 100 ). Non-match → 404 (jaisa hai).
      → face threshold AppSetting.faceMatchThreshold se aaye (hardcoded 0.5 nahi).

### Settings (admin)
- `GET /settings`  → AppSetting (single row; na ho to default banao)
- `PUT /settings`  body:{faceMatchThreshold?, requireLiveness?} → AppSetting
- `GET  /attendance/today/{employeeId}`     → AttendanceDay (aaj ka)
- `GET  /attendance/daily?date=YYYY-MM-DD`  → AttendanceDay[] (sab employees us din)
- `GET  /attendance/report?month=YYYY-MM&employeeId={id}` → MonthlyReport
- `POST /attendance/recompute?date=YYYY-MM-DD&employeeId={id}` → AttendanceDay (dobara calc)

### Attendance — MANUAL correction (admin)
- `GET    /attendance/punches?date=YYYY-MM-DD&employeeId={id}` → AttendancePunch[]
- `POST   /attendance/punches`        body:{employeeId,timestamp,direction,note} → AttendancePunch (recompute trigger)
- `PUT    /attendance/punches/{id}`   body:{timestamp,direction,note} → AttendancePunch (recompute)
- `DELETE /attendance/punches/{id}`   → 204 (recompute)
- `PUT    /attendance/day`            body:{employeeId,date,netMinutes?,status?,manualNote} 
      → manual override (isManual=true). netMinutes/status diya to wahi use ho, calc ignore.

### Holidays
- `GET    /holidays?year=2026` → Holiday[]
- `POST   /holidays`           → Holiday
- `PUT    /holidays/{id}`      → Holiday
- `DELETE /holidays/{id}`      → 204

### Leaves
- `GET    /leaves?employeeId={id}&month=YYYY-MM` → LeaveRequest[]
- `POST   /leaves`            → LeaveRequest (status=Pending)
- `PUT    /leaves/{id}`       body:{status?|fromDate?|toDate?|type?|isPaid?|reason?} → LeaveRequest
- `DELETE /leaves/{id}`       → 204

---

## AttendanceCalculator — CORE LOGIC (backend, pure, unit-tested)

Input: employee ke ek din ke punches (sorted) + shift policy + holiday/leave info.
Steps:
1. Punches ko timestamp se sort. Debounce: same direction 2 punch < 60s → dusra ignore.
2. Pair: IN→OUT sessions banao. `grossMinutes = Σ(out-in)`.
3. Last punch IN bina OUT → `hasOpenSession=true` (us session ko gross me mat jodo; flag karo).
4. `breakMinutes` = (lastOut - firstIn) - grossMinutes  (display ke liye).
5. Lunch: agar `autoDeductLunch` → `lunchDeduction = Σ overlap(session, [lunchStart,lunchEnd])`; warna 0.
6. `netMinutes = grossMinutes - lunchDeduction`.
7. Status (UPDATED — "present flow" fix):
   - **Agar us din KOI punch nahi hai:** din holiday → `Holiday`; weeklyOff day → `WeeklyOff`;
     approved leave → `Leave`; warna `Absent`.
   - **Agar punch hai (firstIn mojood):**
       - `hasOpenSession=true` (abhi office me, last punch IN, OUT nahi kiya) → **`Present`**
         (chahe hours abhi kam ho — banda abhi kaam kar raha hai).
       - warna (din band, last punch OUT): net ≥ requiredMinutes → `Present`;
         ≥ halfDayThreshold → `HalfDay`; warna `Absent`.
   > Matlab: punch IN karte hi status `Present` ho jata hai. Yahi user ko chahiye.
8. `isManual=true` ho to override values use karo, calc skip.

Monthly summary: din-wise status count; `payableDays = presentDays + 0.5*halfDays + paidHolidays + paidLeaves + weeklyOffs` (weekly off paid माना).

## Salary (admin-only) — frontend
- Salary data monthly report ke `summary` me aata hai (upar dekho).
- Frontend me ek alag **Salary page** ho jo **admin PIN** maange (simple gate, default PIN `admin123`,
  `environment.adminPin` me configurable). PIN sahi hone par hi salary dikhe.
- Salary page: month + employee chuno → breakdown (monthlySalary, payableDays, perDaySalary,
  earnedSalary, lossOfPay, netPayable) → **Salary Slip PDF** export (jspdf). Yeh attendance PDF se alag hai.
- Baaki pages (kiosk/employees/daily/holidays/leaves) PIN ke bina khulein. Sirf salary protected.

## STRICT rules (dono projects)
- Raw `AttendancePunch` immutable-ish: calculation hamesha punches se derive. Manual edit alag flag.
- DTOs use karo (entity directly expose mat karo). FluentValidation ya DataAnnotations se input validate.
- Async/await sab DB calls. Nullable reference types ON. Warnings = treat seriously.
- Frontend: typed models (interfaces), koi `any` nahi. Feature-wise services. Loading/error states.
- Seed data: 1 default Shift, 2-3 employees, kuch holidays — taaki app khulte hi kuch dikhe.
