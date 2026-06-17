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
  IonItem,
  IonInput,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonNote,
  IonCard,
  IonCardHeader,
  IonCardSubtitle,
  IonCardTitle,
  IonCardContent,
  IonRange,
  IonToggle,
  IonLabel,
  IonList,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { lockClosedOutline, saveOutline, settingsOutline } from 'ionicons/icons';
import { environment } from '../../../environments/environment';
import { SettingsService } from '../../core/settings.service';
import { RoleService } from '../../core/role.service';
import { AppSetting, Role } from '../../core/models';

// Admin-only Settings page. Gated behind a simple client-side PIN
// (environment.adminPin) — same pattern as the Salary page.
// NOTE: this is a BASIC gate for convenience, NOT real authentication.
@Component({
  selector: 'app-settings',
  standalone: true,
  templateUrl: './settings.page.html',
  styleUrls: ['./settings.page.scss'],
  imports: [
    CommonModule,
    FormsModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonItem,
    IonInput,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonNote,
    IonCard,
    IonCardHeader,
    IonCardSubtitle,
    IonCardTitle,
    IonCardContent,
    IonRange,
    IonToggle,
    IonLabel,
    IonList,
  ],
})
export class SettingsPage implements OnInit {
  private settingsSvc = inject(SettingsService);
  private roleSvc = inject(RoleService);
  private toastCtrl = inject(ToastController);

  // ===== Admin PIN gate (basic, client-side only) =====
  unlocked = signal(false);
  pin = '';
  pinError = signal<string | null>(null);

  // ===== Settings state =====
  setting = signal<AppSetting | null>(null);
  threshold = signal(0.5); // editable copy bound to the range slider
  requireLiveness = signal(false); // editable copy bound to the toggle
  voiceEnabled = signal(true); // editable copy bound to the voice toggle
  overtimePayable = signal(false); // editable copy bound to the overtime toggle
  loading = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);

  // Range bounds (per contract: 0.3..0.7).
  readonly minThreshold = 0.3;
  readonly maxThreshold = 0.7;

  // ===== Employee Roles =====
  // Toggling a role's isActive controls whether it appears in the Add Employee dropdown.
  // SHARED signal from RoleService — toggling here updates the employee dropdown instantly.
  roles = this.roleSvc.roles;
  rolesLoading = signal(false);
  rolesError = signal<string | null>(null);
  // IDs currently being updated (disable the toggle while saving).
  savingRoleIds = signal<number[]>([]);

  constructor() {
    addIcons({ lockClosedOutline, saveOutline, settingsOutline });
  }

  ngOnInit(): void {
    // Settings are only loaded after a successful unlock.
  }

  // ===== PIN gate =====
  unlock(): void {
    if (this.pin === environment.adminPin) {
      this.unlocked.set(true);
      this.pinError.set(null);
      this.pin = '';
      this.load();
      this.loadRoles();
    } else {
      this.pinError.set('Incorrect PIN. Please try again.');
    }
  }

  // Load current global settings from the backend.
  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.settingsSvc.get().subscribe({
      next: (s) => {
        this.setting.set(s);
        this.threshold.set(s.faceMatchThreshold);
        this.requireLiveness.set(s.requireLiveness);
        this.voiceEnabled.set(s.voiceEnabled);
        this.overtimePayable.set(s.overtimePayable);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load settings. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  // ion-range emits its value via (ionChange); keep our signal in sync.
  onThresholdChange(value: number | { lower: number; upper: number }): void {
    const v = typeof value === 'number' ? value : value.lower;
    this.threshold.set(v);
  }

  onLivenessChange(checked: boolean): void {
    this.requireLiveness.set(checked);
  }

  onVoiceChange(checked: boolean): void {
    this.voiceEnabled.set(checked);
  }

  onOvertimeChange(checked: boolean): void {
    this.overtimePayable.set(checked);
  }

  thresholdLabel(): string {
    return this.threshold().toFixed(2);
  }

  save(): void {
    const t = this.threshold();
    // Validate threshold stays in the allowed range.
    if (t < this.minThreshold || t > this.maxThreshold) {
      this.toast(`Threshold must be between ${this.minThreshold} and ${this.maxThreshold}.`, 'danger');
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.settingsSvc
      .update({
        faceMatchThreshold: t,
        requireLiveness: this.requireLiveness(),
        voiceEnabled: this.voiceEnabled(),
        overtimePayable: this.overtimePayable(),
      })
      .subscribe({
        next: (s) => {
          this.setting.set(s);
          this.threshold.set(s.faceMatchThreshold);
          this.requireLiveness.set(s.requireLiveness);
          this.voiceEnabled.set(s.voiceEnabled);
          this.overtimePayable.set(s.overtimePayable);
          this.saving.set(false);
          this.toast('Settings saved.', 'success');
        },
        error: () => {
          this.saving.set(false);
          this.toast('Could not save settings. Is the backend running?', 'danger');
        },
      });
  }

  // ===== Employee Roles =====

  // Load all roles (active and inactive) for the toggle list.
  private loadRoles(): void {
    this.rolesLoading.set(true);
    this.rolesError.set(null);
    this.roleSvc.getAll().subscribe({
      next: (r) => {
        this.roles.set(r);
        this.rolesLoading.set(false);
      },
      error: () => {
        this.rolesError.set('Could not load roles. Is the backend running?');
        this.rolesLoading.set(false);
      },
    });
  }

  isRoleSaving(id: number): boolean {
    return this.savingRoleIds().includes(id);
  }

  // Immediate-on-toggle: persist the new active state and reflect it in the list.
  onRoleToggle(role: Role, isActive: boolean): void {
    if (role.isActive === isActive) return;
    this.savingRoleIds.update((ids) => [...ids, role.id]);
    this.roleSvc.update(role.id, { isActive }).subscribe({
      next: (updated) => {
        this.roles.update((list) => list.map((r) => (r.id === updated.id ? updated : r)));
        this.savingRoleIds.update((ids) => ids.filter((i) => i !== role.id));
        this.toast(`Role "${updated.name}" ${updated.isActive ? 'enabled' : 'disabled'}.`, 'success');
      },
      error: () => {
        // Revert the toggle visually by re-emitting the original state.
        this.roles.update((list) => list.map((r) => (r.id === role.id ? { ...r, isActive: role.isActive } : r)));
        this.savingRoleIds.update((ids) => ids.filter((i) => i !== role.id));
        this.toast('Could not update role. Is the backend running?', 'danger');
      },
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
