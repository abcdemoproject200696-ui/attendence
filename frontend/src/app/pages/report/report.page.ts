import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
  IonInput,
  IonSelect,
  IonSelectOption,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonBadge,
  IonCard,
  IonCardHeader,
  IonCardSubtitle,
  IonCardTitle,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { documentTextOutline, downloadOutline, refreshOutline } from 'ionicons/icons';
import { jsPDF } from 'jspdf';
import { autoTable } from 'jspdf-autotable';
import { AttendanceService } from '../../core/attendance.service';
import { EmployeeService } from '../../core/employee.service';
import { Employee, MonthlyReport, DayStatus } from '../../core/models';
import { fmtMinutes, fmtTime, currentMonth } from '../../core/util';

@Component({
  selector: 'app-report',
  standalone: true,
  templateUrl: './report.page.html',
  styleUrls: ['./report.page.scss'],
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
    IonInput,
    IonSelect,
    IonSelectOption,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonBadge,
    IonCard,
    IonCardHeader,
    IonCardSubtitle,
    IonCardTitle,
  ],
})
export class ReportPage implements OnInit {
  private attendance = inject(AttendanceService);
  private employeeSvc = inject(EmployeeService);
  private toastCtrl = inject(ToastController);

  month = currentMonth();
  employeeId: number | null = null;
  employees = signal<Employee[]>([]);
  report = signal<MonthlyReport | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  constructor() {
    addIcons({ documentTextOutline, downloadOutline, refreshOutline });
  }

  ngOnInit(): void {
    this.employeeSvc.getAll().subscribe({
      next: (e) => {
        this.employees.set(e);
        if (e.length && this.employeeId == null) this.employeeId = e[0].id;
      },
      error: () => this.error.set('Could not load employees. Is the backend running?'),
    });
  }

  fmtMin(m: number): string {
    return fmtMinutes(m);
  }
  fmtTime(iso: string | null | undefined): string {
    return fmtTime(iso);
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

  generate(): void {
    if (this.employeeId == null || !this.month) {
      this.error.set('Please select both month and employee.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.report.set(null);
    this.attendance.report(this.month, this.employeeId).subscribe({
      next: (r) => {
        this.report.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load report. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  exportPdf(): void {
    const r = this.report();
    if (!r) return;
    const doc = new jsPDF();
    doc.setFontSize(16);
    doc.text(`Attendance Report — ${r.employeeName}`, 14, 16);
    doc.setFontSize(11);
    doc.text(`Month: ${r.month}`, 14, 24);

    autoTable(doc, {
      startY: 30,
      head: [['Date', 'In', 'Out', 'Gross', 'Lunch −', 'Net', 'Status']],
      body: r.days.map((d) => [
        d.date,
        fmtTime(d.firstIn),
        fmtTime(d.lastOut),
        fmtMinutes(d.grossMinutes),
        d.lunchDeduction > 0 ? `${fmtMinutes(d.lunchDeduction)} (${fmtTime(d.lunchFrom)}-${fmtTime(d.lunchTo)})` : '-',
        fmtMinutes(d.netMinutes),
        d.status,
      ]),
      styles: { fontSize: 8 },
      headStyles: { fillColor: [56, 128, 255] },
    });

    const s = r.summary;
    autoTable(doc, {
      head: [['Summary', 'Value']],
      body: [
        ['Present Days', String(s.presentDays)],
        ['Half Days', String(s.halfDays)],
        ['Absent Days', String(s.absentDays)],
        ['Paid Holidays', String(s.paidHolidays)],
        ['Unpaid Holidays', String(s.unpaidHolidays)],
        ['Paid Leaves', String(s.paidLeaves)],
        ['Unpaid Leaves', String(s.unpaidLeaves)],
        ['Weekly Offs', String(s.weeklyOffs)],
        ['Total Net Hours', fmtMinutes(s.totalNetMinutes)],
        ['Payable Days (hour-based)', s.payableDays.toFixed(2)],
      ],
      styles: { fontSize: 9 },
      headStyles: { fillColor: [56, 128, 255] },
    });

    doc.save(`report-${r.employeeName}-${r.month}.pdf`);
    this.toast('PDF downloaded.', 'success');
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
