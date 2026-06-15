import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginResult } from './models';

// Basic client-side auth (see CONTRACT.md RBAC). NOT a JWT/token flow — login creds
// are verified by the backend and the resulting session (allowedPages) is kept in
// localStorage + a signal for the guard and menu filtering.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private base = `${environment.apiUrl}/auth`;

  private static readonly STORAGE_KEY = 'attendance.session';

  // Current logged-in user (null when logged out).
  readonly currentUser = signal<LoginResult | null>(this.readStored());

  private readStored(): LoginResult | null {
    try {
      const raw = localStorage.getItem(AuthService.STORAGE_KEY);
      return raw ? (JSON.parse(raw) as LoginResult) : null;
    } catch {
      return null;
    }
  }

  login(code: string, password: string): Observable<LoginResult> {
    return this.http.post<LoginResult>(`${this.base}/login`, { code, password }).pipe(
      tap((result) => {
        localStorage.setItem(AuthService.STORAGE_KEY, JSON.stringify(result));
        this.currentUser.set(result);
      }),
    );
  }

  isLoggedIn(): boolean {
    return this.currentUser() !== null;
  }

  // Admin = seeded Role id 1. Used to gate admin-only actions (e.g. delete employee).
  isAdmin(): boolean {
    return this.currentUser()?.roleId === 1;
  }

  // True if the given page key is in the user's allowedPages.
  hasPage(key: string): boolean {
    return this.currentUser()?.allowedPages.includes(key) ?? false;
  }

  // First page key the user is allowed to open (used for post-login / fallback redirects).
  firstAllowedPage(): string | null {
    return this.currentUser()?.allowedPages[0] ?? null;
  }

  logout(): void {
    localStorage.removeItem(AuthService.STORAGE_KEY);
    this.currentUser.set(null);
    void this.router.navigate(['/login']);
  }
}
