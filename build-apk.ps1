# ============================================================================
#  build-apk.ps1  -  Build the Attendance Android APK pointing at your LIVE backend.
#
#  USAGE (PowerShell, from the attendance folder):
#     .\build-apk.ps1 -BackendUrl "https://YOUR-BACKEND.onrender.com/api"
#
#  Output APK: frontend\android\app\build\outputs\apk\debug\app-debug.apk
#  Copy that .apk to your phone and install (allow install from unknown sources).
# ============================================================================
param(
  [Parameter(Mandatory = $true)]
  [string]$BackendUrl
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$frontend = Join-Path $root "frontend"
$envProd = Join-Path $frontend "src\environments\environment.prod.ts"

# Use Android Studio's bundled JDK if JAVA_HOME isn't already a JDK.
if (-not $env:JAVA_HOME -or -not (Test-Path (Join-Path $env:JAVA_HOME "bin\java.exe"))) {
  $env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
}
Write-Host "JAVA_HOME = $env:JAVA_HOME" -ForegroundColor Cyan

# 1) Point the production build at the live backend.
Write-Host "==> Setting apiUrl = $BackendUrl" -ForegroundColor Green
@"
export const environment = {
  production: true,
  apiUrl: '$BackendUrl',
  adminPin: 'admin123',
};
"@ | Set-Content -Encoding utf8 $envProd

Set-Location $frontend

# 2) Production web build.
Write-Host "==> npm run build" -ForegroundColor Green
npm run build
if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }

# 3) Add the Android platform the first time, then sync the web build into it.
if (-not (Test-Path (Join-Path $frontend "android"))) {
  Write-Host "==> npx cap add android (first time)" -ForegroundColor Green
  npx cap add android
}
Write-Host "==> npx cap sync android" -ForegroundColor Green
npx cap sync android

# 4) Build the debug APK via Gradle.
Set-Location (Join-Path $frontend "android")
Write-Host "==> gradlew assembleDebug" -ForegroundColor Green
.\gradlew.bat assembleDebug

$apk = Join-Path $frontend "android\app\build\outputs\apk\debug\app-debug.apk"
if (Test-Path $apk) {
  Write-Host ""
  Write-Host "APK READY:" -ForegroundColor Green
  Write-Host "  $apk"
  Write-Host "Copy it to your phone and install it." -ForegroundColor Cyan
} else {
  Write-Host "APK not found. Check the Gradle output above." -ForegroundColor Red
}
