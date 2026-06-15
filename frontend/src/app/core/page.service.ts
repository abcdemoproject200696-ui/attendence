import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Page } from './models';

// Pages — GET /pages (seeded list of all app pages/menu items).
@Injectable({ providedIn: 'root' })
export class PageService {
  private http = inject(HttpClient);
  private base = `${environment.apiUrl}/pages`;

  getAll(): Observable<Page[]> {
    return this.http.get<Page[]>(this.base);
  }
}
