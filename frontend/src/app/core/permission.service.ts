import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { RolePermissions } from './models';

// Role page-permissions — GET/PUT /roles/{roleId}/permissions.
@Injectable({ providedIn: 'root' })
export class PermissionService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/roles`;

  getForRole(roleId: number): Observable<RolePermissions> {
    return this.http.get<RolePermissions>(`${this.base}/${roleId}/permissions`);
  }

  setForRole(roleId: number, pageIds: number[]): Observable<RolePermissions> {
    return this.http.put<RolePermissions>(`${this.base}/${roleId}/permissions`, { pageIds });
  }
}
