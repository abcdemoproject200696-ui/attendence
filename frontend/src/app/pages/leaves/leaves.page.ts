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
  IonList,
  IonLabel,
  IonBadge,
  IonToggle,
  IonModal,
  AlertController,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  addOutline,
  trashOutline,
  checkmarkOutline,
  closeOutline,
  airplaneOutline,
} from 'ionicons/icons';
import { LeaveService } from '../../core/leave.service';
import { EmployeeService } from '../../core/employee.service';
import { Employee, LeaveRequest, LeaveInput, LeaveType, LeaveStatus } from '../../core/models';

@Component({
  selector: 'app-leaves',
  standalone: true,
  templateUrl: './leaves.page.html',
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
    IonList,
    IonLabel,
    IonBadge,
    IonToggle,
    IonModal,
  ],
})
export class LeavesPage implements OnInit {
  private leaveSvc = inject(LeaveService);
  private employeeSvc = inject(EmployeeService);
  private alertCtrl = inject(AlertController);
  private toastCtrl = inject(ToastController);

  readonly leaveTypes: LeaveType[] = ['Casual', 'Sick', 'Paid', 'Unpaid'];

  filterEmployeeId: number | null = null;
  leaves = signal<LeaveRequest[]>([]);
  employees = signal<Employee[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  modalOpen = signal(false);
  saving = signal(false);
  form: LeaveInput = this.blankForm();

  constructor() {
    addIcons({ addOutline, trashOutline, checkmarkOutline, closeOutline, airplaneOutline });
  }

  ngOnInit(): void {
    this.employeeSvc.getAll().subscribe({
      next: (e) => this.employees.set(e),
      error: () => {
        /* leaves load error covers backend-down */
      },
    });
    this.load();
  }

  private blankForm(): LeaveInput {
    return { employeeId: 0, fromDate: '', toDate: '', type: 'Casual', isPaid: true, reason: '' };
  }

  employeeName(id: number): string {
    return this.employees().find((e) => e.id === id)?.name ?? `#${id}`;
  }

  statusColor(s: LeaveStatus): string {
    return s === 'Approved' ? 'success' : s === 'Rejected' ? 'danger' : 'warning';
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.leaveSvc.getAll(this.filterEmployeeId ?? undefined).subscribe({
      next: (l) => {
        this.leaves.set(l);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load leaves. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  openNew(): void {
    this.form = this.blankForm();
    if (this.employees().length) this.form.employeeId = this.employees()[0].id;
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  apply(): void {
    if (!this.form.employeeId || !this.form.fromDate || !this.form.toDate) {
      this.toast('Employee, From and To dates are required.', 'warning');
      return;
    }
    this.saving.set(true);
    this.leaveSvc.create(this.form).subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast('Leave applied (Pending).', 'success');
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.toast('Failed to apply.', 'danger');
      },
    });
  }

  setStatus(l: LeaveRequest, status: LeaveStatus): void {
    this.leaveSvc.update(l.id, { status }).subscribe({
      next: () => {
        this.toast(`Leave ${status}.`, 'success');
        this.load();
      },
      error: () => this.toast('Failed to update.', 'danger'),
    });
  }

  togglePaid(l: LeaveRequest): void {
    this.leaveSvc.update(l.id, { isPaid: !l.isPaid }).subscribe({
      next: () => this.load(),
      error: () => this.toast('Failed to update.', 'danger'),
    });
  }

  async confirmDelete(l: LeaveRequest): Promise<void> {
    const a = await this.alertCtrl.create({
      header: 'Delete leave?',
      message: `${this.employeeName(l.employeeId)} · ${l.fromDate} → ${l.toDate}`,
      buttons: [
        { text: 'Cancel', role: 'cancel' },
        {
          text: 'Delete',
          role: 'destructive',
          handler: () =>
            this.leaveSvc.delete(l.id).subscribe({
              next: () => {
                this.toast('Deleted.', 'success');
                this.load();
              },
              error: () => this.toast('Failed to delete.', 'danger'),
            }),
        },
      ],
    });
    await a.present();
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
