import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonBackButton,
  IonTitle,
  IonContent,
  IonGrid,
  IonRow,
  IonCol,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonBadge,
  IonCard,
  IonCardHeader,
  IonCardSubtitle,
  IonCardTitle,
  IonCardContent,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { downloadOutline, shareOutline, logInOutline, logOutOutline } from 'ionicons/icons';
import { jsPDF } from 'jspdf';
import { autoTable } from 'jspdf-autotable';
import { Capacitor } from '@capacitor/core';
import { Share } from '@capacitor/share';
import { Filesystem, Directory } from '@capacitor/filesystem';
import { AttendanceService } from '../../core/attendance.service';
import { AttendanceDay, AttendancePunch, DayStatus } from '../../core/models';
import { fmtMinutes, fmtTime, todayIso } from '../../core/util';

// One IN -> OUT (or open) work session derived from a day's punches.
interface Session {
  inAt: string;
  outAt: string | null;
  minutes: number; // 0 if still open
  open: boolean;
}

// Employee daily attendance DETAIL: every punch, in/out counts, sessions,
// total time in office, plus PDF export + native share.
@Component({
  selector: 'app-attendance-detail',
  standalone: true,
  templateUrl: './attendance-detail.page.html',
  styleUrls: ['./attendance-detail.page.scss'],
  imports: [
    CommonModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonBackButton,
    IonTitle,
    IonContent,
    IonGrid,
    IonRow,
    IonCol,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonBadge,
    IonCard,
    IonCardHeader,
    IonCardSubtitle,
    IonCardTitle,
    IonCardContent,
  ],
})
export class AttendanceDetailPage implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private attendance = inject(AttendanceService);
  private toastCtrl = inject(ToastController);

  employeeId = 0;
  date = todayIso();
  displayName = '';

  loading = signal(false);
  error = signal<string | null>(null);
  exporting = signal(false);

  punches = signal<AttendancePunch[]>([]);
  day = signal<AttendanceDay | null>(null);

  // Derived employee identity (prefer the day/punch data, fall back to query param).
  name = computed(() => this.day()?.employeeName || this.punches()[0]?.employeeName || this.displayName || 'Employee');
  code = computed(() => this.punches().find((p) => p.employeeCode)?.employeeCode ?? '');

  inCount = computed(() => this.punches().filter((p) => p.direction === 'IN').length);
  outCount = computed(() => this.punches().filter((p) => p.direction === 'OUT').length);

  // Pair consecutive IN -> OUT into sessions; a trailing IN with no OUT is "still in".
  sessions = computed<Session[]>(() => {
    const sorted = [...this.punches()].sort(
      (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime(),
    );
    const result: Session[] = [];
    let openIn: AttendancePunch | null = null;
    for (const p of sorted) {
      if (p.direction === 'IN') {
        if (!openIn) openIn = p;
      } else if (p.direction === 'OUT' && openIn) {
        const minutes = Math.max(
          0,
          Math.round((new Date(p.timestamp).getTime() - new Date(openIn.timestamp).getTime()) / 60000),
        );
        result.push({ inAt: openIn.timestamp, outAt: p.timestamp, minutes, open: false });
        openIn = null;
      }
    }
    if (openIn) result.push({ inAt: openIn.timestamp, outAt: null, minutes: 0, open: true });
    return result;
  });

  // Sorted punches for display (oldest first).
  sortedPunches = computed(() =>
    [...this.punches()].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()),
  );

  constructor() {
    addIcons({ downloadOutline, shareOutline, logInOutline, logOutOutline });
  }

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParamMap;
    this.employeeId = Number(qp.get('employeeId') ?? 0);
    this.date = qp.get('date') || todayIso();
    this.displayName = qp.get('name') ?? '';
    if (!this.employeeId) {
      this.error.set('No employee selected.');
      return;
    }
    this.load();
  }

  fmtMin(m: number): string {
    return fmtMinutes(m);
  }
  fmtTime(iso: string | null | undefined): string {
    return fmtTime(iso);
  }

  // "Monday, 16 June 2026"
  prettyDate(iso: string): string {
    const d = new Date(`${iso}T00:00:00`);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString([], { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });
  }

  statusColor(s: DayStatus | undefined): string {
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
    this.loading.set(true);
    this.error.set(null);

    // The day endpoint differs: today/{id} for today, recompute(date, id) for past dates
    // (there is no GET /attendance/day; recompute returns the calculated AttendanceDay).
    const day$ =
      this.date === todayIso()
        ? this.attendance.today(this.employeeId)
        : this.attendance.recompute(this.date, this.employeeId);

    forkJoin({
      punches: this.attendance.getPunches(this.date, this.employeeId).pipe(catchError(() => of([] as AttendancePunch[]))),
      day: day$.pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ punches, day }) => {
        this.punches.set(punches);
        this.day.set(day);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load attendance detail. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  private fileName(): string {
    const safe = (this.name() || 'employee').replace(/[^a-z0-9]+/gi, '-').toLowerCase();
    return `attendance-${safe}-${this.date}.pdf`;
  }

  // Build the PDF document (shared by download + share).
  private buildPdf(): jsPDF {
    const doc = new jsPDF();
    const d = this.day();
    const codePart = this.code() ? ` (${this.code()})` : '';

    doc.setFontSize(16);
    doc.text(`Attendance — ${this.name()}${codePart}`, 14, 18);
    doc.setFontSize(11);
    doc.text(this.prettyDate(this.date), 14, 26);

    autoTable(doc, {
      startY: 32,
      head: [['Summary', 'Value']],
      body: [
        ['Status', d?.status ?? '-'],
        ['Checked In', `${this.inCount()} time(s)`],
        ['Checked Out', `${this.outCount()} time(s)`],
        ['First In', fmtTime(d?.firstIn)],
        ['Last Out', fmtTime(d?.lastOut)],
        ['Total Time In Office', fmtMinutes(d?.netMinutes ?? 0)],
        ['Gross', fmtMinutes(d?.grossMinutes ?? 0)],
        ['Break', fmtMinutes(d?.breakMinutes ?? 0)],
      ],
      styles: { fontSize: 9 },
      headStyles: { fillColor: [56, 128, 255] },
    });

    autoTable(doc, {
      head: [['#', 'Direction', 'Time']],
      body: this.sortedPunches().map((p, i) => [
        String(i + 1),
        p.direction === 'IN' ? 'CHECK IN' : 'CHECK OUT',
        fmtTime(p.timestamp),
      ]),
      styles: { fontSize: 10 },
      headStyles: { fillColor: [45, 211, 111] },
    });

    return doc;
  }

  // Export: native -> write to cache + Share; web -> download (or navigator.share if available).
  async exportPdf(): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    try {
      const doc = this.buildPdf();
      const fileName = this.fileName();
      const title = `Attendance — ${this.name()} — ${this.date}`;

      if (Capacitor.isNativePlatform()) {
        const base64 = doc.output('datauristring').split(',')[1];
        await Filesystem.writeFile({ path: fileName, data: base64, directory: Directory.Cache });
        const { uri } = await Filesystem.getUri({ path: fileName, directory: Directory.Cache });
        await Share.share({ title, text: title, url: uri });
        this.toast('Shared.', 'success');
      } else {
        doc.save(fileName);
        this.toast('PDF downloaded.', 'success');
      }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      // A user cancelling the native share sheet should not be reported as an error.
      if (!/cancel|abort/i.test(msg)) this.toast('Could not export PDF.', 'danger');
    } finally {
      this.exporting.set(false);
    }
  }

  back(): void {
    this.router.navigate(['/daily']);
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
