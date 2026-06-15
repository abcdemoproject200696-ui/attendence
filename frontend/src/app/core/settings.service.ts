import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AppSetting } from './models';

// Global app settings (single row): face match threshold + liveness requirement.
// GET /settings (backend creates a default row if none) and PUT /settings.
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/settings`;

  get(): Observable<AppSetting> {
    return this.http.get<AppSetting>(this.base);
  }

  update(body: Partial<AppSetting>): Observable<AppSetting> {
    return this.http.put<AppSetting>(this.base, body);
  }
}
