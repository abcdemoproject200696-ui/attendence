import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { LeaveRequest, LeaveInput, LeaveUpdate } from './models';

// Leaves — GET ?employeeId=&month= / POST / PUT / DELETE
@Injectable({ providedIn: 'root' })
export class LeaveService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/leaves`;

  getAll(employeeId?: number, month?: string): Observable<LeaveRequest[]> {
    let params = new HttpParams();
    if (employeeId != null) params = params.set('employeeId', employeeId);
    if (month) params = params.set('month', month);
    return this.http.get<LeaveRequest[]>(this.base, { params });
  }

  create(data: LeaveInput): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(this.base, data);
  }

  update(id: number, data: LeaveUpdate): Observable<LeaveRequest> {
    return this.http.put<LeaveRequest>(`${this.base}/${id}`, data);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
