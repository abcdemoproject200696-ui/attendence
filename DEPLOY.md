# Attendance — Deploy Guide (Live URL + APK)

Ye project **restaurant se bilkul alag** hai (apna git repo, apni deploy, apna URL).
Restaurant ka URL waise hi rahega; attendance ka **naya URL** banega.

```
Frontend (Ionic/Angular)  →  Netlify   →  https://your-attendance.netlify.app   ← naya URL
Backend  (.NET 10 + Postgres) → Render →  https://attendance-api.onrender.com/api
APK (mobile)              →  build-apk.ps1  →  app-debug.apk  →  phone
```

> **2 alag accounts chahiye (free):** GitHub, Render. (Netlify bhi free — GitHub se login.)

---

## STEP 1 — Code GitHub pe daalo (alag repo)

1. GitHub pe naya **empty repo** banao, e.g. `attendance-system` (README/gitignore add mat karna).
2. Apne PC pe (PowerShell, attendance folder me):
```powershell
cd D:\TestProject\attendance
git branch -M main
git remote add origin https://github.com/<TUMHARA-USERNAME>/attendance-system.git
git push -u origin main
```
> Ye repo restaurant se alag hai — isme push karne se restaurant ke code pe koi asar nahi.

---

## STEP 2 — Backend deploy (Render + free PostgreSQL)

1. https://render.com → GitHub se login.
2. **New ➜ Blueprint** ➜ apna `attendance-system` repo choose karo.
3. Render `render.yaml` padh kar khud bana dega: **ek free PostgreSQL** + **ek web service** (`attendance-api`).
4. **Apply / Create** dabao. ~3-5 min me build hoga (pehli baar Docker build slow).
5. Ban-ne ke baad backend URL milega, e.g. `https://attendance-api.onrender.com`.
   - Test: browser me `https://attendance-api.onrender.com/api/pages` kholo → 10 pages ka JSON dikhe = backend live ✅
   - Data ab **PostgreSQL** me save hoga (restart pe reset nahi hoga).

> **Note:** Render free tier ~50s "cold start" leta hai agar app der tak idle rahe (pehli request slow). Ye normal hai.

---

## STEP 3 — Frontend ko backend se jodo

`frontend/src/environments/environment.prod.ts` me `apiUrl` ko apne **Render backend URL** se badlo:
```ts
apiUrl: 'https://attendance-api.onrender.com/api',
```
Phir commit + push:
```powershell
cd D:\TestProject\attendance
git commit -am "Set production backend URL"
git push
```

---

## STEP 4 — Frontend deploy (Netlify → naya URL)

1. https://netlify.com → GitHub se login.
2. **Add new site ➜ Import an existing project** ➜ `attendance-system` repo.
3. Netlify `frontend/netlify.toml` se settings le lega (base `frontend`, build `npm run build`, publish `www`).
   - Agar manually pooche: **Base directory** = `frontend`, **Build command** = `npm run build`, **Publish directory** = `frontend/www`.
4. **Deploy** ➜ 2-3 min me site live. URL milega, e.g. `https://your-attendance.netlify.app` ← **yahi tumhara naya alag URL hai.**
5. Browser me kholo → app khulega (Attendance scan page). Login: `EMP001 / admin123`.

> Camera (face scan) ke liye HTTPS chahiye — Netlify/Render dono https dete hain, to chal jayega.

---

## STEP 5 — Mobile APK banao aur phone me daalo

PC pe (PowerShell, attendance folder), apna **Render backend URL** do:
```powershell
cd D:\TestProject\attendance
.\build-apk.ps1 -BackendUrl "https://attendance-api.onrender.com/api"
```
- Pehli baar Android platform add karega (thoda time), phir APK banega:
  `frontend\android\app\build\outputs\apk\debug\app-debug.apk`
- Is `app-debug.apk` file ko phone me copy karo (USB/WhatsApp/Drive), aur install karo
  ("unknown sources / unknown apps install" allow karna padega).
- App khulte hi camera face-scan — internet se backend se connect ho jayega.

> APK humesha **live backend URL** ke saath banao (localhost se phone pe kaam nahi karega).

---

## Quick reference

| Cheez | Value |
|---|---|
| Admin login | `EMP001` / `admin123` |
| Doosre seeded employees | password `pass123` |
| Salary/Settings admin PIN | `admin123` (`environment.prod.ts`) |
| Backend health check | `<backend-url>/api/pages` |
| Backend API docs | `<backend-url>/swagger` |

## Baad me code change karne par
- Code badlo → `git commit` → `git push` → Render + Netlify **apne aap** naya deploy kar denge.
- Mobile app update chahiye → `build-apk.ps1` dobaara chalao → naya APK phone me daalo.
