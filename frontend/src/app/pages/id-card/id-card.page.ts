import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonMenuButton,
  IonTitle,
  IonContent,
  IonSearchbar,
  IonList,
  IonItem,
  IonAvatar,
  IonLabel,
  IonIcon,
  IonButton,
  IonSpinner,
  IonText,
  IonNote,
  IonModal,
  IonDatetime,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  personCircleOutline,
  checkmarkCircle,
  downloadOutline,
  calendarOutline,
} from 'ionicons/icons';
import { jsPDF } from 'jspdf';
import { EmployeeService } from '../../core/employee.service';
import { Employee } from '../../core/models';
import { todayIso } from '../../core/util';

// Employee ID Card generator. Mirrors the Flutter app's ID Card screen:
// pick an employee, set the validity window, preview the green Tech Anusiya card
// and download it as a print-ready PDF (credit-card proportions). RBAC: reuses the
// "employees" page key, so the same admins/HR who manage staff can issue cards.
@Component({
  selector: 'app-id-card',
  standalone: true,
  templateUrl: './id-card.page.html',
  styleUrls: ['./id-card.page.scss'],
  imports: [
    CommonModule,
    FormsModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonSearchbar,
    IonList,
    IonItem,
    IonAvatar,
    IonLabel,
    IonIcon,
    IonButton,
    IonSpinner,
    IonText,
    IonNote,
    IonModal,
    IonDatetime,
  ],
})
export class IdCardPage implements OnInit {
  private employeeSvc = inject(EmployeeService);
  private toastCtrl = inject(ToastController);

  loading = signal(false);
  error = signal<string | null>(null);
  employees = signal<Employee[]>([]);
  query = signal('');

  selected = signal<Employee | null>(null);

  // Issued = today (read-only). Valid till defaults to today + 1 year (admin-editable).
  validFrom = signal(todayIso());
  validTill = signal(this.plusOneYear(todayIso()));
  tillPickerOpen = signal(false);

  constructor() {
    addIcons({ personCircleOutline, checkmarkCircle, downloadOutline, calendarOutline });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.employeeSvc.getAll().subscribe({
      next: (e) => {
        this.employees.set(e);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load employees. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  // Employees matching the search query (name, phone, or code).
  filtered = computed<Employee[]>(() => {
    const q = this.query().trim().toLowerCase();
    const list = this.employees();
    if (!q) return list;
    return list.filter(
      (e) =>
        e.name.toLowerCase().includes(q) ||
        (e.phone ?? '').toLowerCase().includes(q) ||
        e.code.toLowerCase().includes(q)
    );
  });

  select(emp: Employee): void {
    this.selected.set(emp);
  }

  // ===== Valid Till picker =====
  get tillForPicker(): string {
    return `${this.validTill()}T00:00:00`;
  }

  onTillSelected(value: string | string[] | null | undefined): void {
    const v = Array.isArray(value) ? value[0] : value;
    if (v) this.validTill.set(v.slice(0, 10));
    this.tillPickerOpen.set(false);
  }

  // "yyyy-MM-dd" -> "dd MMM yyyy" for display.
  fmtDate(iso: string | null | undefined): string {
    if (!iso) return '-';
    const d = new Date(`${iso}T00:00:00`);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private plusOneYear(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    const next = new Date(y + 1, (m || 1) - 1, d || 1);
    const yy = next.getFullYear();
    const mm = (next.getMonth() + 1).toString().padStart(2, '0');
    const dd = next.getDate().toString().padStart(2, '0');
    return `${yy}-${mm}-${dd}`;
  }

  // ===== PDF — credit-card proportions, centered on an A6 page =====
  downloadPdf(): void {
    const e = this.selected();
    if (!e) return;

    // A6 portrait page in mm; the card is drawn centered with a small margin.
    const doc = new jsPDF({ unit: 'mm', format: 'a6' });
    const pageW = doc.internal.pageSize.getWidth();
    const pageH = doc.internal.pageSize.getHeight();

    const GREEN: [number, number, number] = [22, 163, 74]; // #16A34A
    const GREEN_SOFT: [number, number, number] = [232, 248, 239];
    const INK: [number, number, number] = [33, 37, 41];
    const MUTED: [number, number, number] = [120, 120, 120];

    const margin = 6;
    const cardX = margin;
    const cardW = pageW - margin * 2;
    const cardY = margin;
    const cardH = pageH - margin * 2;
    const cx = cardX + cardW / 2;

    // Card background + border (rounded).
    doc.setFillColor(255, 255, 255);
    doc.setDrawColor(200, 200, 200);
    doc.setLineWidth(0.3);
    doc.roundedRect(cardX, cardY, cardW, cardH, 4, 4, 'FD');

    // ===== Header band =====
    const headH = 16;
    doc.setFillColor(...GREEN);
    doc.roundedRect(cardX, cardY, cardW, headH, 4, 4, 'F');
    doc.rect(cardX, cardY + headH - 4, cardW, 4, 'F'); // square off the bottom of the band
    // "TA" logo badge.
    doc.setFillColor(255, 255, 255);
    doc.roundedRect(cardX + 4, cardY + 3.5, 9, 9, 2, 2, 'F');
    doc.setTextColor(...GREEN);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(9);
    doc.text('TA', cardX + 8.5, cardY + 9.3, { align: 'center' });
    // Title + subtitle.
    doc.setTextColor(255, 255, 255);
    doc.setFontSize(11);
    doc.text('TECH ANUSIYA', cardX + 16, cardY + 7.5);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(6.5);
    doc.text('Employee Identity Card', cardX + 16, cardY + 11.8);

    // ===== Photo =====
    const photoSize = 26;
    const photoX = cx - photoSize / 2;
    const photoY = cardY + headH + 5;
    doc.setFillColor(...GREEN_SOFT);
    doc.setDrawColor(...GREEN);
    doc.setLineWidth(0.5);
    doc.roundedRect(photoX, photoY, photoSize, photoSize, 2, 2, 'FD');
    if (e.photoUrl && e.photoUrl.startsWith('data:image')) {
      try {
        const fmt = e.photoUrl.includes('image/png') ? 'PNG' : 'JPEG';
        doc.addImage(e.photoUrl, fmt, photoX, photoY, photoSize, photoSize);
      } catch {
        /* if the image is unreadable, leave the soft-green placeholder box */
      }
    } else {
      doc.setTextColor(...MUTED);
      doc.setFontSize(6);
      doc.text('PHOTO', cx, photoY + photoSize / 2 + 1, { align: 'center' });
    }

    // ===== Name =====
    let y = photoY + photoSize + 6;
    doc.setTextColor(...INK);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.text(e.name || '-', cx, y, { align: 'center' });

    // ===== Fields =====
    y += 5;
    const labelX = cardX + 8;
    const valueX = cardX + 30;
    const rows: Array<[string, string]> = [
      ['Emp ID', e.code || '-'],
      ['Designation', e.roleName || '-'],
      ['Blood Group', e.bloodGroup || '-'],
      ['Date of Birth', this.fmtDate(e.dob)],
      ['Mobile', e.phone || '-'],
    ];
    for (const [label, value] of rows) {
      doc.setFont('helvetica', 'normal');
      doc.setFontSize(7);
      doc.setTextColor(...MUTED);
      doc.text(label, labelX, y);
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(8);
      doc.setTextColor(...INK);
      doc.text(value, valueX, y);
      y += 5.2;
    }

    // ===== Validity strip =====
    y += 1;
    const stripH = 8;
    doc.setFillColor(...GREEN_SOFT);
    doc.roundedRect(cardX + 6, y, cardW - 12, stripH, 1.5, 1.5, 'F');
    doc.setTextColor(...GREEN);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(6.6);
    doc.text(
      `Valid From: ${this.validFrom()}     Valid Till: ${this.validTill()}`,
      cx,
      y + stripH / 2 + 1,
      { align: 'center' }
    );

    // ===== Footer band =====
    const footH = 8;
    const footY = cardY + cardH - footH;
    doc.setFillColor(...GREEN);
    doc.rect(cardX, footY, cardW, footH - 4, 'F');
    doc.roundedRect(cardX, footY, cardW, footH, 4, 4, 'F');
    doc.rect(cardX, footY, cardW, footH - 4, 'F');
    doc.setTextColor(255, 255, 255);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(6.2);
    doc.text('Tech Anusiya  -  Innovating Together', cx, footY + footH / 2 + 1, { align: 'center' });

    doc.save(`${(e.name || 'Employee').replace(/[^A-Za-z0-9]+/g, '_')}_ID_Card.pdf`);
    this.toast('ID card PDF downloaded.', 'success');
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
