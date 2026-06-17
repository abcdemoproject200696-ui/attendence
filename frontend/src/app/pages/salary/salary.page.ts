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

  // Money WITH 2 decimals — for per-day / per-hour where paise matter (e.g. ₹2,666.67).
  inrDec(n: number): string {
    return `₹${(n ?? 0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  // PDF-safe money. jsPDF's built-in fonts have NO Rupee (₹) glyph — it renders as a
  // broken "¹" and pushes the number off the page. So the PDF uses "Rs" instead.
  private inrPdf(n: number): string {
    return `Rs ${Math.round(n ?? 0).toLocaleString('en-IN')}`;
  }
  private inrPdfDec(n: number): string {
    return `Rs ${(n ?? 0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  // "2026-06" -> "June 2026" for the slip header + file name.
  private monthLong(month: string): string {
    const [y, m] = month.split('-').map(Number);
    const names = [
      'January', 'February', 'March', 'April', 'May', 'June',
      'July', 'August', 'September', 'October', 'November', 'December',
    ];
    return m >= 1 && m <= 12 ? `${names[m - 1]} ${y}` : month;
  }

  // A full working day in hours, e.g. 480 min -> "8".
  reqHours(min: number): string {
    return (min / 60).toFixed(min % 60 === 0 ? 0 : 1);
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

  // Last drawn autoTable's bottom Y (typed accessor for jsPDF + autotable plugin).
  private finalY(doc: jsPDF): number {
    return (doc as unknown as { lastAutoTable: { finalY: number } }).lastAutoTable.finalY;
  }

  exportSlipPdf(): void {
    const r = this.report();
    if (!r) return;
    const s = r.summary;
    const doc = new jsPDF();
    const pageW = doc.internal.pageSize.getWidth();

    // Brand palette (matches the on-screen card).
    const GREEN: [number, number, number] = [45, 211, 111];
    const GREEN_SOFT: [number, number, number] = [232, 248, 239];
    const BLUE: [number, number, number] = [56, 128, 255];
    const BLUE_SOFT: [number, number, number] = [235, 242, 255];
    const INK: [number, number, number] = [33, 37, 41];
    const MUTED: [number, number, number] = [120, 120, 120];
    const reqH = this.reqHours(s.requiredMinutesPerDay);

    // ===== Coloured header band =====
    doc.setFillColor(...GREEN);
    doc.rect(0, 0, pageW, 32, 'F');
    doc.setTextColor(255, 255, 255);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(22);
    doc.text('SALARY SLIP', 14, 16);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(11);
    doc.text(`Month: ${this.monthLong(r.month)}`, 14, 25);
    doc.setFontSize(12);
    doc.setFont('helvetica', 'bold');
    doc.text('Attendance System', pageW - 14, 16, { align: 'right' });

    // ===== Employee identity =====
    doc.setTextColor(...INK);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(15);
    doc.text(r.employeeName, 14, 45);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(...MUTED);
    doc.text(`Employee Code: ${this.employeeCode()}`, 14, 51);

    // ===== Attendance summary =====
    autoTable(doc, {
      startY: 57,
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
        ['Total Hours Worked', fmtMinutes(s.totalNetMinutes)],
        ['Payable Days', s.payableDays.toFixed(2)],
      ],
      theme: 'striped',
      styles: { fontSize: 9.5, cellPadding: 2.6, textColor: INK },
      headStyles: { fillColor: BLUE, textColor: 255, fontStyle: 'bold' },
      alternateRowStyles: { fillColor: BLUE_SOFT },
      columnStyles: { 1: { halign: 'right', fontStyle: 'bold' } },
    });

    // ===== Salary calculation (method shown inline in row labels) =====
    autoTable(doc, {
      startY: this.finalY(doc) + 5,
      head: [['Salary Calculation', 'Amount']],
      body: [
        ['Monthly Salary', this.inrPdf(s.monthlySalary)],
        ['Total Days in Month', String(s.totalDaysInMonth)],
        ['Per Day  =  Monthly / Days', this.inrPdfDec(s.perDaySalary)],
        ['Working Hours / Day', `${reqH} h`],
        ['Per Hour  =  Per Day / Hours', this.inrPdfDec(s.perHourSalary)],
        ['Total Hours Worked', fmtMinutes(s.totalNetMinutes)],
        ['Payable Days (hour-based)', s.payableDays.toFixed(2)],
        ['Earned  =  Per Day x Payable Days', this.inrPdf(s.earnedSalary)],
      ],
      theme: 'striped',
      styles: { fontSize: 9.5, cellPadding: 2.6, textColor: INK },
      headStyles: { fillColor: GREEN, textColor: 255, fontStyle: 'bold' },
      alternateRowStyles: { fillColor: GREEN_SOFT },
      columnStyles: { 1: { halign: 'right', fontStyle: 'bold' } },
    });

    // ===== NET PAYABLE banner (big, green, like the on-screen card) =====
    let y = this.finalY(doc) + 7;
    doc.setFillColor(...GREEN);
    doc.roundedRect(14, y, pageW - 28, 18, 3, 3, 'F');
    doc.setTextColor(255, 255, 255);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(14);
    doc.text('NET PAYABLE', 22, y + 11.5);
    doc.setFontSize(17);
    doc.text(this.inrPdf(s.netPayable), pageW - 22, y + 11.5, { align: 'right' });

    // ===== Calculation method (so the shared slip explains itself) =====
    y += 28;
    doc.setTextColor(...MUTED);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8.6);
    const method =
      `How this is calculated:\n` +
      `1. Per Day Salary = Monthly Salary / ${s.totalDaysInMonth} days = ${this.inrPdfDec(s.perDaySalary)}.\n` +
      `2. Per Hour Salary = Per Day / ${reqH} working hours = ${this.inrPdfDec(s.perHourSalary)}.\n` +
      `3. Each present day is paid by the actual hours worked (hours / ${reqH}h), capped at one full day. ` +
      `Overtime is paid only when "Overtime payable" is enabled in Settings.\n` +
      `4. Net Payable = Per Day x Payable Days (${s.payableDays.toFixed(2)}) = ${this.inrPdf(s.netPayable)}.`;
    doc.text(doc.splitTextToSize(method, pageW - 28) as string[], 14, y);

    // Thin footer rule + note.
    doc.setDrawColor(...GREEN);
    doc.setLineWidth(0.6);
    doc.line(14, 286, pageW - 14, 286);
    doc.setFontSize(7.5);
    doc.setTextColor(...MUTED);
    doc.text('Computer-generated salary slip — Attendance System.', 14, 291);

    doc.save(`${r.employeeName} Salary Slip ${this.monthLong(r.month)}.pdf`);
    this.toast('Salary slip PDF downloaded.', 'success');
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
