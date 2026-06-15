import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Shift, ShiftInput } from './models';

// Shifts — GET/POST/PUT /shifts
@Injectable({ providedIn: 'root' })
export class ShiftService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/shifts`;

  getAll(): Observable<Shift[]> {
    return this.http.get<Shift[]>(this.base);
  }

  getById(id: number): Observable<Shift> {
    return this.http.get<Shift>(`${this.base}/${id}`);
  }

  create(data: ShiftInput): Observable<Shift> {
    return this.http.post<Shift>(this.base, data);
  }

  update(id: number, data: ShiftInput): Observable<Shift> {
    return this.http.put<Shift>(`${this.base}/${id}`, data);
  }
}
