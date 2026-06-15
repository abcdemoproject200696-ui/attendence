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
  IonLabel,
  IonInput,
  IonSelect,
  IonSelectOption,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonNote,
  IonBadge,
  IonCard,
  IonCardHeader,
  IonCardSubtitle,
  IonCardTitle,
  IonCardContent,
  IonDatetime,
  IonModal,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { lockClosedOutline, cashOutline, downloadOutline, calendarOutline } from 'ionicons/icons';
import { jsPDF } from 'jspdf';
import { autoTable } from 'jspdf-autotable';
import { environment } from '../../../environments/environment';
import { AttendanceService } from '../../core/attendance.service';
import { EmployeeService } from '../../core/employee.service';
import { Employee, MonthlyReport } from '../../core/models';
import { fmtMinutes, currentMonth } from '../../core/util';

// Admin-only Salary page. Gated behind a simple client-side PIN (environment.adminPin).
// NOTE: this is a BASIC gate for convenience, NOT real authentication/authorization.
@Component({
  selector: 'app-salary',
  standalone: true,
  templateUrl: './salary.page.html',
  styleUrls: ['./salary.page.scss'],
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
    IonSelect,
    IonSelectOption,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonNote,
    IonBadge,
    IonCard,
    IonCardHeader,
    IonCardSubtitle,
    IonCardTitle,
    IonCardContent,
    IonDatetime,
    IonModal,
  ],
})
export class SalaryPage implements OnInit {
  private attendance = inject(AttendanceService);
  private employeeSvc = inject(EmployeeService);
  private toastCtrl = inject(ToastController);

  // ===== Admin PIN gate (basic, client-side only) =====
  unlocked = signal(false);
  pin = '';
  pinError = signal<string | null>(null);

  // ===== Salary view state =====
  month = currentMonth();
  employeeId: number | null = null;
  employees = signal<Employee[]>([]);
  report = signal<MonthlyReport | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);

  // month-picker modal
  monthPickerOpen = signal(false);

  constructor() {
    addIcons({ lockClosedOutline, cashOutline, downloadOutline, calendarOutline });
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

  // ===== PIN gate =====
  unlock(): void {
    if (this.pin === environment.adminPin) {
      this.unlocked.set(true);
      this.pinError.set(null);
      this.pin = '';
    } else {
      this.pinError.set('Incorrect PIN. Please try again.');
    }
  }

  // ===== Month picker =====
  get monthForDatetime(): string {
    return `${this.month}-01`;
  }

  onMonthSelected(value: string | string[] | null | undefined): void {
    const v = Array.isArray(value) ? value[0] : value;
    if (v) this.month = v.slice(0, 7);
    this.monthPickerOpen.set(false);
  }

  employeeName(): string {
    return this.employees().find((e) => e.id === this.employeeId)?.name ?? '';
  }

  employeeCode(): string {
    return this.employees().find((e) => e.id === this.employeeId)?.code ?? '';
  }

  fmtMin(m: number): string {
    return fmtMinutes(m);
  }

  inr(n: number): string {
    return `₹${(n ?? 0).toLocaleString('en-IN')}`;
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
        this.error.set('Could not load salary report. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  exportSlipPdf(): void {
    const r = this.report();
    if (!r) return;
    const s = r.summary;
    const doc = new jsPDF();

    doc.setFontSize(18);
    doc.text('Salary Slip', 14, 18);
    doc.setFontSize(11);
    doc.text(`Employee: ${r.employeeName} (${this.employeeCode()})`, 14, 28);
    doc.text(`Month: ${r.month}`, 14, 35);

    // Attendance summary
    autoTable(doc, {
      startY: 42,
      head: [['Attendance', 'Value']],
      body: [
        ['Present Days', String(s.presentDays)],
        ['Half Days', String(s.halfDays)],
        ['Absent Days', String(s.absentDays)],
        ['Paid Leaves', String(s.paidLeaves)],
        ['Unpaid Leaves', String(s.unpaidLeaves)],
        ['Paid Holidays', String(s.paidHolidays)],
        ['Unpaid Holidays', String(s.unpaidHolidays)],
        ['Weekly Offs', String(s.weeklyOffs)],
        ['Payable Days', String(s.payableDays)],
        ['Total Net Hours', fmtMinutes(s.totalNetMinutes)],
      ],
      styles: { fontSize: 9 },
      headStyles: { fillColor: [56, 128, 255] },
    });

    // Salary breakdown
    autoTable(doc, {
      head: [['Salary Breakdown', 'Amount']],
      body: [
        ['Monthly Salary', this.inr(s.monthlySalary)],
        ['Total Days In Month', String(s.totalDaysInMonth)],
        ['Per Day Salary', this.inr(Math.round(s.perDaySalary))],
        ['Payable Days', String(s.payableDays)],
        ['Earned Salary', this.inr(s.earnedSalary)],
        ['Loss Of Pay', this.inr(s.lossOfPay)],
        ['NET PAYABLE', this.inr(s.netPayable)],
      ],
      styles: { fontSize: 10 },
      headStyles: { fillColor: [45, 211, 111] },
      didParseCell: (data) => {
        if (data.section === 'body' && data.row.index === 6) {
          data.cell.styles.fontStyle = 'bold';
          data.cell.styles.fillColor = [224, 247, 233];
        }
      },
    });

    doc.save(`salary-slip-${r.employeeName}-${r.month}.pdf`);
    this.toast('Salary slip PDF downloaded.', 'success');
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
