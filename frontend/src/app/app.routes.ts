import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  // Login is the default landing for logged-out users; the guard bounces guarded
  // routes here when there is no session.
  {
    path: 'login',
    loadComponent: () => import('./pages/login/login.page').then((m) => m.LoginPage),
  },
  // Public self-service signup — reachable while logged out (NO guard).
  {
    path: 'signup',
    loadComponent: () => import('./pages/signup/signup.page').then((m) => m.SignupPage),
  },
  // PUBLIC door-scanner landing: opening the app shows the face-scan kiosk (no login).
  { path: '', redirectTo: 'kiosk', pathMatch: 'full' },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    data: { pageKey: 'dashboard' },
    loadComponent: () => import('./pages/dashboard/dashboard.page').then((m) => m.DashboardPage),
  },
  // Kiosk is PUBLIC (no guard) — it's the door attendance scanner. Recognized faces
  // punch in/out; unknown faces are sent to signup; a Login link is shown for staff.
  {
    path: 'kiosk',
    data: { pageKey: 'kiosk' },
    loadComponent: () => import('./pages/kiosk/kiosk.page').then((m) => m.KioskPage),
  },
  {
    path: 'employees',
    canActivate: [authGuard],
    data: { pageKey: 'employees' },
    loadComponent: () => import('./pages/employees/employees.page').then((m) => m.EmployeesPage),
  },
  {
    path: 'daily',
    canActivate: [authGuard],
    data: { pageKey: 'daily' },
    loadComponent: () => import('./pages/daily/daily.page').then((m) => m.DailyPage),
  },
  {
    path: 'attendance-detail',
    canActivate: [authGuard],
    data: { pageKey: 'daily' },
    loadComponent: () =>
      import('./pages/attendance-detail/attendance-detail.page').then((m) => m.AttendanceDetailPage),
  },
  {
    path: 'report',
    canActivate: [authGuard],
    data: { pageKey: 'report' },
    loadComponent: () => import('./pages/report/report.page').then((m) => m.ReportPage),
  },
  {
    path: 'holidays',
    canActivate: [authGuard],
    data: { pageKey: 'holidays' },
    loadComponent: () => import('./pages/holidays/holidays.page').then((m) => m.HolidaysPage),
  },
  {
    path: 'leaves',
    canActivate: [authGuard],
    data: { pageKey: 'leaves' },
    loadComponent: () => import('./pages/leaves/leaves.page').then((m) => m.LeavesPage),
  },
  {
    path: 'salary',
    canActivate: [authGuard],
    data: { pageKey: 'salary' },
    loadComponent: () => import('./pages/salary/salary.page').then((m) => m.SalaryPage),
  },
  {
    path: 'settings',
    canActivate: [authGuard],
    data: { pageKey: 'settings' },
    loadComponent: () => import('./pages/settings/settings.page').then((m) => m.SettingsPage),
  },
  {
    path: 'permissions',
    canActivate: [authGuard],
    data: { pageKey: 'permissions' },
    loadComponent: () => import('./pages/permissions/permissions.page').then((m) => m.PermissionsPage),
  },
  { path: '**', redirectTo: 'kiosk' },
];
