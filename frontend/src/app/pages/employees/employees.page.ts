import { Component, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonMenuButton,
  IonTitle,
  IonContent,
  IonButton,
  IonIcon,
  IonList,
  IonItem,
  IonLabel,
  IonBadge,
  IonSpinner,
  IonText,
  IonModal,
  IonFab,
  IonFabButton,
  AlertController,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  addOutline,
  createOutline,
  trashOutline,
  personCircleOutline,
} from 'ionicons/icons';
import { EmployeeService } from '../../core/employee.service';
import { ShiftService } from '../../core/shift.service';
import { AuthService } from '../../core/auth.service';
import { Employee, EmployeeInput, Shift } from '../../core/models';
import { EmployeeFormComponent } from './employee-form.component';

@Component({
  selector: 'app-employees',
  standalone: true,
  templateUrl: './employees.page.html',
  imports: [
    CommonModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonButton,
    IonIcon,
    IonList,
    IonItem,
    IonLabel,
    IonBadge,
    IonSpinner,
    IonText,
    IonModal,
    IonFab,
    IonFabButton,
    EmployeeFormComponent,
  ],
})
export class EmployeesPage implements OnInit {
  @ViewChild('employeeForm') employeeForm?: EmployeeFormComponent;

  private employeeSvc = inject(EmployeeService);
  private shiftSvc = inject(ShiftService);
  private alertCtrl = inject(AlertController);
  private toastCtrl = inject(ToastController);
  private auth = inject(AuthService);

  // Only an Admin may delete employees (delete button is hidden otherwise).
  isAdmin(): boolean {
    return this.auth.isAdmin();
  }

  loading = signal(false);
  error = signal<string | null>(null);
  employees = signal<Employee[]>([]);
  shifts = signal<Shift[]>([]);

  modalOpen = signal(false);
  saving = signal(false);
  // The employee being edited (null => add mode). Drives the shared form's @Input.
  editing = signal<Employee | null>(null);

  constructor() {
    addIcons({ addOutline, createOutline, trashOutline, personCircleOutline });
  }

  ngOnInit(): void {
    this.load();
  }

  shiftName(id: number): string {
    return this.shifts().find((s) => s.id === id)?.name ?? '—';
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.shiftSvc.getAll().subscribe({
      next: (s) => this.shifts.set(s),
      error: () => {
        /* employee load error already covers backend-down */
      },
    });
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

  openNew(): void {
    this.editing.set(null);
    this.modalOpen.set(true);
  }

  openEdit(emp: Employee): void {
    this.editing.set(emp);
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.employeeForm?.stopCamera();
    this.modalOpen.set(false);
  }

  // Save button -> ask the shared form to validate + emit its payload via onSubmit().
  triggerSave(): void {
    this.employeeForm?.submit();
  }

  // Receives the validated EmployeeInput from the shared form and persists it.
  onSubmit(payload: EmployeeInput): void {
    const editing = this.editing();
    this.saving.set(true);
    const obs =
      editing == null
        ? this.employeeSvc.create(payload)
        : this.employeeSvc.update(editing.id, payload);
    obs.subscribe({
      next: () => {
        this.saving.set(false);
        this.employeeForm?.stopCamera();
        this.modalOpen.set(false);
        this.toast('Saved.', 'success');
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.toast('Save failed. Check the backend or for a duplicate code.', 'danger');
      },
    });
  }

  async confirmDelete(emp: Employee): Promise<void> {
    const alert = await this.alertCtrl.create({
      header: 'Delete employee?',
      message: `Delete ${emp.name} (${emp.code})?`,
      buttons: [
        { text: 'Cancel', role: 'cancel' },
        {
          text: 'Delete',
          role: 'destructive',
          handler: () => this.doDelete(emp.id),
        },
      ],
    });
    await alert.present();
  }

  private doDelete(id: number): void {
    this.employeeSvc.delete(id).subscribe({
      next: () => {
        this.toast('Deleted.', 'success');
        this.load();
      },
      error: () => this.toast('Failed to delete.', 'danger'),
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
