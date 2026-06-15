import {
  Component,
  OnInit,
  OnDestroy,
  Input,
  Output,
  EventEmitter,
  ViewChild,
  ElementRef,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  IonList,
  IonItem,
  IonInput,
  IonNote,
  IonSelect,
  IonSelectOption,
  IonToggle,
  IonLabel,
  IonText,
  IonIcon,
  IonButton,
  IonSpinner,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { cameraOutline, checkmarkCircle, refreshOutline, eyeOutline, eyeOffOutline } from 'ionicons/icons';
import { ShiftService } from '../../core/shift.service';
import { RoleService } from '../../core/role.service';
import { FaceService } from '../../core/face.service';
import { Employee, EmployeeInput, Shift, Role } from '../../core/models';

// Reusable Add/Edit employee form body. Used by BOTH the Employees page modal
// (edit + add) and the public Signup page. Owns the role/shift dropdowns,
// password field and multi-photo face enrollment. Emits the EmployeeInput
// payload on submit and exposes validity for the host's Save button.
@Component({
  selector: 'app-employee-form',
  standalone: true,
  templateUrl: './employee-form.component.html',
  styles: [
    `
      .enroll-section {
        border-top: 1px solid var(--ion-color-step-150, #e0e0e0);
        margin-top: 8px;
      }
      .enroll-cam-wrap {
        width: 100%;
        aspect-ratio: 4 / 3;
        background: #000;
        border-radius: 10px;
        overflow: hidden;
        margin-bottom: 10px;
      }
      .enroll-cam {
        width: 100%;
        height: 100%;
        object-fit: cover;
      }
      .enroll-hint {
        font-size: 0.8rem;
        margin: 2px 0 8px;
      }
      .enroll-progress {
        display: flex;
        align-items: center;
        gap: 4px;
        font-weight: 500;
        margin: 4px 0 8px;
      }
      .field-err {
        font-size: 0.78rem;
        margin: 2px 0 6px 6px;
      }
    `,
  ],
  imports: [
    CommonModule,
    FormsModule,
    IonList,
    IonItem,
    IonInput,
    IonNote,
    IonSelect,
    IonSelectOption,
    IonToggle,
    IonLabel,
    IonText,
    IonIcon,
    IonButton,
    IonSpinner,
  ],
})
export class EmployeeFormComponent implements OnInit, OnDestroy {
  @ViewChild('enrollVideo') enrollVideoRef?: ElementRef<HTMLVideoElement>;

  // Existing employee for edit mode; null/undefined => add mode.
  @Input() employee: Employee | null = null;
  // When true (signup), the password field is required and labelled accordingly.
  @Input() passwordRequired = false;
  // Show the read-only auto-code row (employees modal). Signup hides it.
  @Input() showCodeRow = true;
  // Emits a ready-to-send EmployeeInput payload when the host triggers submit().
  @Output() formSubmit = new EventEmitter<EmployeeInput>();

  private shiftSvc = inject(ShiftService);
  private roleSvc = inject(RoleService);
  private toastCtrl = inject(ToastController);
  private face = inject(FaceService);

  shifts = signal<Shift[]>([]);
  // SHARED signal from RoleService — reflects Settings role toggles instantly.
  roles = this.roleSvc.roles;

  editingId: number | null = null;
  form: EmployeeInput = this.blankForm();

  // Mirrors form.roleId so the role-options computed reacts to selection changes.
  selectedRoleId = signal(0);

  // Options for the Role dropdown: only ACTIVE roles, PLUS the currently-selected
  // role if it has become inactive (so an editing employee's role isn't lost).
  roleOptions = computed<Role[]>(() => {
    const active = this.roles().filter((r) => r.isActive);
    const selectedId = this.selectedRoleId();
    if (selectedId && !active.some((r) => r.id === selectedId)) {
      const current = this.roles().find((r) => r.id === selectedId);
      if (current) return [...active, current];
    }
    return active;
  });

  // Face-enroll state.
  cameraOn = signal(false);
  enrollBusy = signal(false);
  capturedFaces = signal<number[][]>([]);
  existingFaceCount = signal(0);

  readonly minFaces = 3;
  readonly maxFaces = 5;

  // ===== Validation =====
  submitted = signal(false); // true after a submit attempt — drives inline errors
  showPassword = signal(false); // eye toggle for the password field
  private readonly emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  private readonly phoneRe = /^[6-9]\d{9}$/; // Indian 10-digit mobile (starts 6-9)

  constructor() {
    addIcons({ cameraOutline, checkmarkCircle, refreshOutline, eyeOutline, eyeOffOutline });
  }

  ngOnInit(): void {
    this.loadLookups();
    this.initFromEmployee();
  }

  ngOnDestroy(): void {
    this.face.stopCamera();
  }

  // Password is required when signing up OR adding a new employee; optional on edit.
  get passwordIsRequired(): boolean {
    return this.passwordRequired || this.editingId == null;
  }

  get passwordLabel(): string {
    return this.passwordIsRequired ? 'Login Password *' : 'Set New Password';
  }

  get passwordPlaceholder(): string {
    return this.passwordIsRequired ? 'Choose a login password' : 'Leave blank to keep current';
  }

  // Field-level validators (used for inline errors + overall validity).
  emailOk(): boolean {
    return this.emailRe.test((this.form.email ?? '').trim());
  }

  phoneOk(): boolean {
    return this.phoneRe.test((this.form.phone ?? '').trim());
  }

  // True only when ALL required fields are valid (host binds its Save button to this).
  get isValid(): boolean {
    if (!this.form.name.trim() || !this.form.roleId || !this.form.shiftId) return false;
    if (!this.emailOk() || !this.phoneOk()) return false;
    if (this.form.monthlySalary == null || this.form.monthlySalary <= 0) return false;
    if (this.passwordIsRequired && (!this.form.password || !this.form.password.trim())) return false;
    return true;
  }

  private blankForm(): EmployeeInput {
    return {
      code: '',
      name: '',
      roleId: 0,
      email: '',
      phone: '',
      shiftId: 0,
      monthlySalary: 0,
      isActive: true,
      password: '',
    };
  }

  private loadLookups(): void {
    this.shiftSvc.getAll().subscribe({
      next: (s) => {
        this.shifts.set(s);
        if (!this.form.shiftId && s.length) this.form.shiftId = s[0].id;
      },
      error: () => {
        /* backend-down surfaced by host */
      },
    });
    this.roleSvc.getAll().subscribe({
      next: (r) => {
        this.roles.set(r);
        if (!this.form.roleId) {
          const firstActive = r.find((role) => role.isActive);
          if (firstActive) this.setRoleId(firstActive.id);
        }
      },
      error: () => {
        /* backend-down surfaced by host */
      },
    });
  }

  // Populate the form from the @Input employee (edit) or reset to blank (add).
  private initFromEmployee(): void {
    const emp = this.employee;
    if (emp) {
      this.editingId = emp.id;
      this.form = {
        code: emp.code,
        name: emp.name,
        roleId: emp.roleId,
        email: emp.email,
        phone: emp.phone,
        shiftId: emp.shiftId,
        monthlySalary: emp.monthlySalary,
        isActive: emp.isActive,
        password: '',
      };
      this.selectedRoleId.set(emp.roleId);
      this.resetFaceEnroll();
      this.existingFaceCount.set(emp.faceCount);
    } else {
      this.editingId = null;
      this.form = this.blankForm();
      if (this.shifts().length) this.form.shiftId = this.shifts()[0].id;
      const firstActive = this.roles().find((r) => r.isActive);
      this.setRoleId(firstActive ? firstActive.id : 0);
      this.resetFaceEnroll();
    }
  }

  // Keep form.roleId and the selectedRoleId signal in sync (signal drives roleOptions).
  setRoleId(id: number): void {
    this.form.roleId = id;
    this.selectedRoleId.set(id);
  }

  private resetFaceEnroll(): void {
    this.face.stopCamera();
    this.cameraOn.set(false);
    this.enrollBusy.set(false);
    this.capturedFaces.set([]);
    this.existingFaceCount.set(0);
    delete this.form.faceDescriptors;
  }

  // Discard all captured photos so the user can recapture from scratch.
  clearCaptures(): void {
    this.capturedFaces.set([]);
    delete this.form.faceDescriptors;
  }

  // ===== Face enrollment =====

  async startEnrollCamera(): Promise<void> {
    const video = this.enrollVideoRef?.nativeElement;
    if (!video) {
      this.toast('Camera element not ready. Close and reopen the form.', 'warning');
      return;
    }
    this.enrollBusy.set(true);
    try {
      await this.face.startCamera(video);
      this.cameraOn.set(true);
      this.face.loadModels().catch(() => undefined);
    } catch (e) {
      this.toast(e instanceof Error ? e.message : 'Could not start the camera.', 'danger');
      this.cameraOn.set(false);
    } finally {
      this.enrollBusy.set(false);
    }
  }

  async captureFace(): Promise<void> {
    const video = this.enrollVideoRef?.nativeElement;
    if (!video || !this.cameraOn()) return;
    if (this.capturedFaces().length >= this.maxFaces) return;
    this.enrollBusy.set(true);
    try {
      const descriptor = await this.face.getDescriptor(video);
      if (!descriptor) {
        this.toast('No face detected, try again.', 'warning');
        return;
      }
      const next = [...this.capturedFaces(), descriptor];
      this.capturedFaces.set(next);
      if (next.length >= this.maxFaces) {
        this.toast(`Maximum ${this.maxFaces} captured.`, 'success');
      } else {
        const hint = next.length >= this.minFaces ? '' : ` — capture at least ${this.minFaces} for best accuracy`;
        this.toast(`Captured ${next.length} of ${this.maxFaces}${hint}.`, 'success');
      }
    } catch {
      this.toast('Face capture failed. Try again.', 'danger');
    } finally {
      this.enrollBusy.set(false);
    }
  }

  async recaptureFace(): Promise<void> {
    this.clearCaptures();
    this.existingFaceCount.set(0);
    await this.startEnrollCamera();
  }

  // Stop the camera (host calls this when closing the modal/page).
  stopCamera(): void {
    this.face.stopCamera();
    this.cameraOn.set(false);
  }

  // Build the EmployeeInput payload and emit it. Returns false (and toasts) when
  // required fields are missing so the host can abort.
  submit(): boolean {
    this.submitted.set(true);
    if (!this.form.name.trim()) {
      this.toast('Name is required.', 'warning');
      return false;
    }
    if (!this.form.roleId) {
      this.toast('Role is required.', 'warning');
      return false;
    }
    if (!this.emailOk()) {
      this.toast('Enter a valid email address.', 'warning');
      return false;
    }
    if (!this.phoneOk()) {
      this.toast('Enter a valid 10-digit mobile number (starts with 6-9).', 'warning');
      return false;
    }
    if (!this.form.shiftId) {
      this.toast('Shift is required.', 'warning');
      return false;
    }
    if (this.form.monthlySalary == null || this.form.monthlySalary <= 0) {
      this.toast('Monthly salary is required.', 'warning');
      return false;
    }
    if (this.passwordIsRequired && (!this.form.password || !this.form.password.trim())) {
      this.toast('Password is required.', 'warning');
      return false;
    }

    const captured = this.capturedFaces();
    if (captured.length > 0) {
      this.form.faceDescriptors = captured;
      if (captured.length < this.minFaces) {
        this.toast(`Tip: ${this.minFaces}+ photos improve recognition. Saving anyway.`, 'warning');
      }
    } else {
      delete this.form.faceDescriptors;
    }

    const payload: EmployeeInput = { ...this.form };
    // NEW employees: send empty code so the backend auto-generates "EMP00X".
    if (this.editingId == null) {
      delete payload.code;
    }
    // Password: omit when blank (EDIT keeps existing; add without password = none).
    if (!payload.password || !payload.password.trim()) {
      delete payload.password;
    }
    if (payload.faceDescriptors) {
      payload.faceDescriptors = payload.faceDescriptors.map((d) => [...d]);
    }

    this.formSubmit.emit(payload);
    return true;
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
