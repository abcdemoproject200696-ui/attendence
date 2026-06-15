import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Holiday, HolidayInput } from './models';

// Holidays — GET ?year= / POST / PUT / DELETE
@Injectable({ providedIn: 'root' })
export class HolidayService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/holidays`;

  getByYear(year: number): Observable<Holiday[]> {
    const params = new HttpParams().set('year', year);
    return this.http.get<Holiday[]>(this.base, { params });
  }

  create(data: HolidayInput): Observable<Holiday> {
    return this.http.post<Holiday>(this.base, data);
  }

  update(id: number, data: HolidayInput): Observable<Holiday> {
    return this.http.put<Holiday>(`${this.base}/${id}`, data);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
