import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonMenuButton,
  IonTitle,
  IonContent,
  IonGrid,
  IonRow,
  IonCol,
  IonItem,
  IonLabel,
  IonInput,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonBadge,
  IonList,
  IonModal,
  IonSelect,
  IonSelectOption,
  ToastController,
  AlertController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { buildOutline, addOutline, trashOutline, createOutline, refreshOutline, saveOutline, eyeOutline } from 'ionicons/icons';
import { AttendanceService } from '../../core/attendance.service';
import { EmployeeService } from '../../core/employee.service';
import { AuthService } from '../../core/auth.service';
import { SettingsService } from '../../core/settings.service';
import { AttendanceDay, AttendancePunch, DayStatus, Direction, Employee } from '../../core/models';
import { fmtMinutes, fmtTime, todayIso } from '../../core/util';

@Component({
  selector: 'app-daily',
  standalone: true,
  templateUrl: './daily.page.html',
  styleUrls: ['./daily.page.scss'],
  imports: [
    CommonModule,
    FormsModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonGrid,
    IonRow,
    IonCol,
    IonItem,
    IonLabel,
    IonInput,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonBadge,
    IonList,
    IonModal,
    IonSelect,
    IonSelectOption,
  ],
})
export class DailyPage implements OnInit {
  private attendance = inject(AttendanceService);
  private employeeSvc = inject(EmployeeService);
  private auth = inject(AuthService);
  private settingsSvc = inject(SettingsService);
  private toastCtrl = inject(ToastController);
  private alertCtrl = inject(AlertController);
  private router = inject(Router);

  // ===== Manual-edit permission =====
  // Admin (roleId 1) can always edit. HR (roleId 2) can edit only when the admin has
  // enabled the hrCanEditAttendance setting. Everyone else: view only.
  private hrCanEditAttendance = signal(false);
  canEditAttendance = computed(() => {
    if (this.auth.isAdmin()) return true;
    const user = this.auth.currentUser();
    return user?.roleId === 2 && this.hrCanEditAttendance();
  });

  // ===== Filters =====
  // employeeId = null => "All employees" (single-date view). Set => that employee's
  // rows for the whole startDate..endDate range.
  employeeId: number | null = null;
  startDate = todayIso();
  endDate = todayIso();
  employees = signal<Employee[]>([]);

  loading = signal(false);
  error = signal<string | null>(null);
  rows = signal<AttendanceDay[]>([]);

  // Range mode = a single employee picked => first column shows the DATE (not the name).
  rangeMode = computed(() => this.employeeId != null);

  readonly statuses: DayStatus[] = ['Present', 'HalfDay', 'Absent', 'Holiday', 'Leave', 'WeeklyOff'];
  readonly directions: Direction[] = ['IN', 'OUT'];

  // ===== Manual correction modal state =====
  correctOpen = signal(false);
  correctEmployeeId = 0;
  correctEmployeeName = '';
  correctDate = todayIso(); // the row's date being corrected (range mode -> per-row date)
  punches = signal<AttendancePunch[]>([]);
  punchesLoading = signal(false);
  savingPunch = signal(false);
  savingOverride = signal(false);

  // new/edit punch form
  editingPunchId: number | null = null;
  punchTime = ''; // "HH:mm"
  punchDir: Direction = 'IN';
  punchNote = '';

  // day override form
  overrideNet: number | null = null;
  overrideStatus: DayStatus | '' = '';
  overrideNote = '';

  constructor() {
    addIcons({ buildOutline, addOutline, trashOutline, createOutline, refreshOutline, saveOutline, eyeOutline });
  }

  ngOnInit(): void {
    // Load the employee dropdown (for the filter); failure is non-fatal (All-mode still works).
    this.employeeSvc.getAll().subscribe({
      next: (e) => this.employees.set(e),
      error: () => {},
    });
    // Load the HR-edit permission. Backend down -> default false (HR/others get view-only).
    this.settingsSvc.get().subscribe({
      next: (s) => this.hrCanEditAttendance.set(s.hrCanEditAttendance),
      error: () => this.hrCanEditAttendance.set(false),
    });
    this.load();
  }

  fmtMin(m: number): string {
    return fmtMinutes(m);
  }
  fmtTime(iso: string | null | undefined): string {
    return fmtTime(iso);
  }

  employeeName(): string {
    return this.employees().find((e) => e.id === this.employeeId)?.name ?? '';
  }

  // "Mon 16 Jun" — compact label for the date column in range mode.
  shortDate(iso: string): string {
    const d = new Date(`${iso}T00:00:00`);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString([], { weekday: 'short', day: '2-digit', month: 'short' });
  }

  statusColor(s: DayStatus): string {
    switch (s) {
      case 'Present':
        return 'success';
      case 'HalfDay':
        return 'warning';
      case 'Absent':
        return 'danger';
      case 'Holiday':
        return 'tertiary';
      case 'Leave':
        return 'primary';
      case 'WeeklyOff':
        return 'medium';
      default:
        return 'medium';
    }
  }

  load(): void {
    if (!this.startDate) return;
    // Keep endDate sane: never before startDate.
    if (!this.endDate || this.endDate < this.startDate) this.endDate = this.startDate;

    this.loading.set(true);
    this.error.set(null);

    const onErr = () => {
      this.error.set('Could not load attendance data. Is the backend running?');
      this.loading.set(false);
    };
    const onOk = (r: AttendanceDay[]) => {
      this.rows.set(r);
      this.loading.set(false);
    };

    if (this.employeeId != null) {
      // One employee, full date range -> day-wise rows.
      this.attendance.range(this.employeeId, this.startDate, this.endDate).subscribe({ next: onOk, error: onErr });
    } else {
      // All employees for the (single) start date.
      this.attendance.daily(this.startDate).subscribe({ next: onOk, error: onErr });
    }
  }

  // Open the read-only daily detail page for one employee+date (View button).
  viewDetail(row: AttendanceDay, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/attendance-detail'], {
      queryParams: { employeeId: row.employeeId, date: row.date, name: row.employeeName },
    });
  }

  // ===== Manual correction =====
  openCorrect(row: AttendanceDay): void {
    // Gate: only Admin (always) or HR-with-permission may open the correction modal.
    if (!this.canEditAttendance()) return;
    this.correctEmployeeId = row.employeeId;
    this.correctEmployeeName = row.employeeName;
    this.correctDate = row.date;
    this.overrideNet = row.isManual ? row.netMinutes : null;
    this.overrideStatus = row.isManual ? row.status : '';
    this.overrideNote = row.manualNote ?? '';
    this.resetPunchForm();
    this.correctOpen.set(true);
    this.loadPunches();
  }

  closeCorrect(): void {
    this.correctOpen.set(false);
  }

  private loadPunches(): void {
    this.punchesLoading.set(true);
    this.attendance.getPunches(this.correctDate, this.correctEmployeeId).subscribe({
      next: (p) => {
        this.punches.set(p);
        this.punchesLoading.set(false);
      },
      error: () => {
        this.punches.set([]);
        this.punchesLoading.set(false);
      },
    });
  }

  private resetPunchForm(): void {
    this.editingPunchId = null;
    this.punchTime = '';
    this.punchDir = 'IN';
    this.punchNote = '';
  }

  editPunch(p: AttendancePunch): void {
    this.editingPunchId = p.id;
    this.punchTime = fmtTime(p.timestamp);
    this.punchDir = p.direction;
    this.punchNote = p.note ?? '';
  }

  // Combine the row's date + "HH:mm" into a local ISO-ish timestamp "yyyy-MM-ddTHH:mm:00".
  private buildTimestamp(): string | null {
    if (!/^\d{2}:\d{2}$/.test(this.punchTime)) return null;
    return `${this.correctDate}T${this.punchTime}:00`;
  }

  savePunch(): void {
    const ts = this.buildTimestamp();
    if (!ts) {
      this.toast('Enter time in HH:mm format (e.g. 10:05).', 'warning');
      return;
    }
    this.savingPunch.set(true);
    const done = () => {
      this.savingPunch.set(false);
      this.resetPunchForm();
      this.loadPunches();
      this.load();
      this.toast('Punch saved.', 'success');
    };
    const fail = () => {
      this.savingPunch.set(false);
      this.toast('Failed to save punch.', 'danger');
    };
    if (this.editingPunchId == null) {
      this.attendance
        .addPunch({ employeeId: this.correctEmployeeId, timestamp: ts, direction: this.punchDir, note: this.punchNote })
        .subscribe({ next: done, error: fail });
    } else {
      this.attendance
        .editPunch(this.editingPunchId, { timestamp: ts, direction: this.punchDir, note: this.punchNote })
        .subscribe({ next: done, error: fail });
    }
  }

  async confirmDeletePunch(p: AttendancePunch): Promise<void> {
    const a = await this.alertCtrl.create({
      header: 'Delete punch?',
      message: `${p.direction} @ ${fmtTime(p.timestamp)}`,
      buttons: [
        { text: 'Cancel', role: 'cancel' },
        {
          text: 'Delete',
          role: 'destructive',
          handler: () => {
            this.attendance.deletePunch(p.id).subscribe({
              next: () => {
                this.loadPunches();
                this.load();
                this.toast('Punch deleted.', 'success');
              },
              error: () => this.toast('Failed to delete.', 'danger'),
            });
          },
        },
      ],
    });
    await a.present();
  }

  saveOverride(): void {
    this.savingOverride.set(true);
    this.attendance
      .overrideDay({
        employeeId: this.correctEmployeeId,
        date: this.correctDate,
        netMinutes: this.overrideNet ?? undefined,
        status: this.overrideStatus || undefined,
        manualNote: this.overrideNote,
      })
      .subscribe({
        next: () => {
          this.savingOverride.set(false);
          this.load();
          this.toast('Day override saved (isManual=true).', 'success');
        },
        error: () => {
          this.savingOverride.set(false);
          this.toast('Failed to save override.', 'danger');
        },
      });
  }

  recompute(): void {
    this.attendance.recompute(this.correctDate, this.correctEmployeeId).subscribe({
      next: () => {
        this.load();
        this.toast('Recomputed from punches.', 'success');
      },
      error: () => this.toast('Failed to recompute.', 'danger'),
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
