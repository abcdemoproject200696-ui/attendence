# Attendance — Frontend (Ionic 8 + Angular 20)

Face-Scanner Attendance System ka frontend. **Ionic 8 + Angular 20 standalone components**, Capacitor 8.
Sab kuch `CONTRACT.md` (root me) ke models/endpoints/enums ke hisaab se bana hai.

## Backend zaroori

App backend se baat karta hai yahan:

```
http://localhost:5080/api
```

Pehle backend (`attendance/backend`, .NET 10 Web API + SQLite) chalao, warna app me har page par
"Backend chal raha hai?" type error/loading dikhega. Base URL set hai:
`src/environments/environment.ts` → `apiUrl: 'http://localhost:5080/api'`.

## Run karne ke steps

```bash
npm install          # dependencies
npm start            # = ng serve  (http://localhost:4200)
# ya Ionic CLI se:
ionic serve
```

Production build (CI / DOD gate):

```bash
npm run build        # ng build -> www/ me output
```

## Mobile app (Capacitor)

`capacitor.config.ts` present hai (`appId: com.attendance.app`, `webDir: www`).
Android native project banakar sync karne ke liye:

```bash
npm run build
npx cap add android        # ek baar
npx cap sync android       # web build ko native me copy
npx cap open android       # Android Studio me kholo
```

(Native android build mandatory nahi hai — config valid hai.)

## Responsive

- `ion-split-pane` + `ion-menu` side navigation: PC/tablet par menu fixed, mobile par overlay/collapse.
- Saare pages `ion-grid` (size / size-md) use karte hain — mobile, tablet, PC browser par theek dikhega.
- Kiosk page tablet fullscreen ke liye bada UI (webcam preview + bada code input + PUNCH button).

## Structure

```
src/app/
  app.component.*        # split-pane + menu shell
  app.routes.ts         # lazy-loaded standalone routes
  core/
    models.ts           # CONTRACT ke interfaces + enum unions (no any)
    util.ts             # fmtMinutes / fmtTime / todayIso / currentMonth
    employee.service.ts
    shift.service.ts
    attendance.service.ts   # punch, today, daily, report, recompute, punches CRUD, day override
    holiday.service.ts
    leave.service.ts
  pages/
    dashboard/          # aaj ke present/half/absent/leave counts + net hours + links
    kiosk/              # door device: code se punch (full), webcam capture (face matching TODO)
    employees/          # list + add/edit/delete (modal) + shift dropdown
    daily/              # date pick + AttendanceDay table + MANUAL correction modal
    report/             # month + employee -> day-wise grid + summary + Export PDF (jspdf)
    holidays/           # year-wise list + add/edit/delete + paid toggle
    leaves/             # list + apply + approve/reject + paid/unpaid toggle
```

## Notes / TODO

- **Face matching TODO**: Kiosk webcam frame capture karta hai (getUserMedia + canvas), lekin face
  descriptor (number[128]) banane wala recognition model abhi wired nahi hai. Code se punch poora kaam
  karta hai. Face wire karne ke liye `kiosk.page.ts` me `captureFace()` me marked TODO dekho — wahan
  descriptor banakar `punch({ faceDescriptor })` bhej do.
- Manual correction (`daily` page): punches add/edit/delete + day override (net/status/note, isManual=true)
  + recompute — sab usable hai.
