import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonMenuButton,
  IonTitle,
  IonContent,
  IonRefresher,
  IonRefresherContent,
  IonGrid,
  IonRow,
  IonCol,
  IonCard,
  IonCardHeader,
  IonCardTitle,
  IonCardSubtitle,
  IonCardContent,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  peopleOutline,
  checkmarkCircleOutline,
  closeCircleOutline,
  timeOutline,
  scanOutline,
  calendarOutline,
  documentTextOutline,
  refreshOutline,
} from 'ionicons/icons';
import { AttendanceService } from '../../core/attendance.service';
import { EmployeeService } from '../../core/employee.service';
import { AttendanceDay } from '../../core/models';
import { fmtMinutes, todayIso } from '../../core/util';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  templateUrl: './dashboard.page.html',
  imports: [
    CommonModule,
    RouterLink,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonRefresher,
    IonRefresherContent,
    IonGrid,
    IonRow,
    IonCol,
    IonCard,
    IonCardHeader,
    IonCardTitle,
    IonCardSubtitle,
    IonCardContent,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
  ],
})
export class DashboardPage implements OnInit {
  private attendance = inject(AttendanceService);
  private employees = inject(EmployeeService);

  readonly date = todayIso();
  loading = signal(false);
  error = signal<string | null>(null);
  days = signal<AttendanceDay[]>([]);
  totalEmployees = signal<number>(0);

  readonly present = computed(() => this.days().filter((d) => d.status === 'Present').length);
  readonly halfDay = computed(() => this.days().filter((d) => d.status === 'HalfDay').length);
  readonly absent = computed(() => this.days().filter((d) => d.status === 'Absent').length);
  readonly onLeave = computed(() => this.days().filter((d) => d.status === 'Leave').length);
  readonly totalNet = computed(() => this.days().reduce((s, d) => s + d.netMinutes, 0));

  constructor() {
    addIcons({
      peopleOutline,
      checkmarkCircleOutline,
      closeCircleOutline,
      timeOutline,
      scanOutline,
      calendarOutline,
      documentTextOutline,
      refreshOutline,
    });
  }

  ngOnInit(): void {
    this.load();
  }

  fmt(min: number): string {
    return fmtMinutes(min);
  }

  load(event?: CustomEvent): void {
    this.loading.set(true);
    this.error.set(null);
    this.attendance.daily(this.date).subscribe({
      next: (rows) => {
        this.days.set(rows);
        this.loading.set(false);
        (event?.target as HTMLIonRefresherElement | undefined)?.complete();
      },
      error: () => {
        this.error.set('Could not load data. Is the backend running at http://localhost:5080?');
        this.loading.set(false);
        (event?.target as HTMLIonRefresherElement | undefined)?.complete();
      },
    });
    this.employees.getAll().subscribe({
      next: (e) => this.totalEmployees.set(e.length),
      error: () => {
        /* dashboard daily card pehle hi error dikha dega */
      },
    });
  }
}
