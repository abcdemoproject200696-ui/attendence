import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IonHeader,
  IonToolbar,
  IonButtons,
  IonMenuButton,
  IonTitle,
  IonContent,
  IonItem,
  IonLabel,
  IonSelect,
  IonSelectOption,
  IonList,
  IonCheckbox,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonNote,
  IonCard,
  IonCardContent,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { saveOutline, shieldCheckmarkOutline } from 'ionicons/icons';
import { RoleService } from '../../core/role.service';
import { PageService } from '../../core/page.service';
import { PermissionService } from '../../core/permission.service';
import { Role, Page } from '../../core/models';

// Admin-only page to assign which pages each role can access.
// Admin role (roleId 1) always has full access (enforced by backend); shown read-only here.
@Component({
  selector: 'app-permissions',
  standalone: true,
  templateUrl: './permissions.page.html',
  styleUrls: ['./permissions.page.scss'],
  imports: [
    FormsModule,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonMenuButton,
    IonTitle,
    IonContent,
    IonItem,
    IonLabel,
    IonSelect,
    IonSelectOption,
    IonList,
    IonCheckbox,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonNote,
    IonCard,
    IonCardContent,
  ],
})
export class PermissionsPage implements OnInit {
  private roleSvc = inject(RoleService);
  private pageSvc = inject(PageService);
  private permissionSvc = inject(PermissionService);
  private toastCtrl = inject(ToastController);

  private readonly adminRoleId = 1;

  // SHARED signal from RoleService — stays in sync with Settings role changes.
  roles = this.roleSvc.roles;
  pages = signal<Page[]>([]);
  roleId: number | null = null;

  // pageId -> checked
  checked = signal<Record<number, boolean>>({});

  loading = signal(false);
  loadingPerms = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);

  constructor() {
    addIcons({ saveOutline, shieldCheckmarkOutline });
  }

  ngOnInit(): void {
    this.loading.set(true);
    this.error.set(null);
    this.roleSvc.getAll().subscribe({
      next: (r) => this.roles.set(r),
      error: () => this.error.set('Could not load roles. Is the backend running?'),
    });
    this.pageSvc.getAll().subscribe({
      next: (p) => {
        this.pages.set([...p].sort((a, b) => a.menuOrder - b.menuOrder));
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load pages. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  isAdminRole(): boolean {
    return this.roleId === this.adminRoleId;
  }

  onRoleChange(): void {
    if (this.roleId == null) return;
    // Admin always has all pages; reflect that visually (checkboxes disabled).
    if (this.isAdminRole()) {
      const all: Record<number, boolean> = {};
      for (const p of this.pages()) all[p.id] = true;
      this.checked.set(all);
      return;
    }
    this.loadingPerms.set(true);
    this.permissionSvc.getForRole(this.roleId).subscribe({
      next: (perms) => {
        const map: Record<number, boolean> = {};
        for (const p of this.pages()) map[p.id] = perms.pageIds.includes(p.id);
        this.checked.set(map);
        this.loadingPerms.set(false);
      },
      error: () => {
        this.loadingPerms.set(false);
        void this.toast('Could not load permissions.', 'danger');
      },
    });
  }

  toggle(pageId: number, value: boolean): void {
    this.checked.set({ ...this.checked(), [pageId]: value });
  }

  save(): void {
    if (this.roleId == null || this.isAdminRole()) return;
    const pageIds = this.pages()
      .filter((p) => this.checked()[p.id])
      .map((p) => p.id);
    this.saving.set(true);
    this.permissionSvc.setForRole(this.roleId, pageIds).subscribe({
      next: () => {
        this.saving.set(false);
        void this.toast('Permissions saved.', 'success');
      },
      error: () => {
        this.saving.set(false);
        void this.toast('Save failed. Is the backend running?', 'danger');
      },
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
