import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AttendanceDay,
  AttendancePunch,
  MonthlyReport,
  PunchRequest,
  PunchResult,
  PunchInput,
  PunchEdit,
  DayOverride,
} from './models';

// Attendance: punch, today, daily, report, recompute, manual punches CRUD, day override.
@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/attendance`;

  // ===== Punch & view =====
  punch(req: PunchRequest): Observable<PunchResult> {
    return this.http.post<PunchResult>(`${this.base}/punch`, req);
  }

  today(employeeId: number): Observable<AttendanceDay> {
    return this.http.get<AttendanceDay>(`${this.base}/today/${employeeId}`);
  }

  daily(date: string): Observable<AttendanceDay[]> {
    const params = new HttpParams().set('date', date);
    return this.http.get<AttendanceDay[]>(`${this.base}/daily`, { params });
  }

  report(month: string, employeeId: number): Observable<MonthlyReport> {
    const params = new HttpParams().set('month', month).set('employeeId', employeeId);
    return this.http.get<MonthlyReport>(`${this.base}/report`, { params });
  }

  recompute(date: string, employeeId: number): Observable<AttendanceDay> {
    const params = new HttpParams().set('date', date).set('employeeId', employeeId);
    return this.http.post<AttendanceDay>(`${this.base}/recompute`, {}, { params });
  }

  // ===== Manual correction (admin) =====
  getPunches(date: string, employeeId: number): Observable<AttendancePunch[]> {
    const params = new HttpParams().set('date', date).set('employeeId', employeeId);
    return this.http.get<AttendancePunch[]>(`${this.base}/punches`, { params });
  }

  addPunch(data: PunchInput): Observable<AttendancePunch> {
    return this.http.post<AttendancePunch>(`${this.base}/punches`, data);
  }

  editPunch(id: number, data: PunchEdit): Observable<AttendancePunch> {
    return this.http.put<AttendancePunch>(`${this.base}/punches/${id}`, data);
  }

  deletePunch(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/punches/${id}`);
  }

  overrideDay(data: DayOverride): Observable<AttendanceDay> {
    return this.http.put<AttendanceDay>(`${this.base}/day`, data);
  }
}
