import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Role } from './models';

// Roles — GET /roles (software-company designations; seeded).
// Holds a SHARED reactive `roles` signal so any page reading it (e.g. the
// employee dropdown) updates instantly when the admin toggles a role in Settings.
@Injectable({ providedIn: 'root' })
export class RoleService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/roles`;

  // Shared state — read this (roleService.roles()) for live updates.
  readonly roles = signal<Role[]>([]);

  // Fetch all roles and publish into the shared signal.
  getAll(): Observable<Role[]> {
    return this.http.get<Role[]>(this.base).pipe(tap((list) => this.roles.set(list)));
  }

  // PUT /roles/{id} — update name and/or active state; patch the shared signal
  // in place so all consumers (settings + employee dropdown) react immediately.
  update(id: number, body: { name?: string; isActive?: boolean }): Observable<Role> {
    return this.http
      .put<Role>(`${this.base}/${id}`, body)
      .pipe(tap((updated) => this.roles.update((list) => list.map((r) => (r.id === id ? updated : r)))));
  }
}
