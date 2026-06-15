import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Employee, EmployeeInput } from './models';

// Employees CRUD — GET/POST/PUT/DELETE /employees
@Injectable({ providedIn: 'root' })
export class EmployeeService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/employees`;

  getAll(): Observable<Employee[]> {
    return this.http.get<Employee[]>(this.base);
  }

  getById(id: number): Observable<Employee> {
    return this.http.get<Employee>(`${this.base}/${id}`);
  }

  create(data: EmployeeInput): Observable<Employee> {
    return this.http.post<Employee>(this.base, data);
  }

  update(id: number, data: EmployeeInput): Observable<Employee> {
    return this.http.put<Employee>(`${this.base}/${id}`, data);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
