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
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonList,
  IonLabel,
  IonToggle,
  IonModal,
  AlertController,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { addOutline, createOutline, trashOutline, sunnyOutline } from 'ionicons/icons';
import { HolidayService } from '../../core/holiday.service';
import { Holiday, HolidayInput } from '../../core/models';

@Component({
  selector: 'app-holidays',
  standalone: true,
  templateUrl: './holidays.page.html',
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
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonList,
    IonLabel,
    IonToggle,
    IonModal,
  ],
})
export class HolidaysPage implements OnInit {
  private holidaySvc = inject(HolidayService);
  private alertCtrl = inject(AlertController);
  private toastCtrl = inject(ToastController);

  year = new Date().getFullYear();
  holidays = signal<Holiday[]>([]);
  loading = signal(false);
  error = signal<string | null>(null);

  modalOpen = signal(false);
  saving = signal(false);
  editingId: number | null = null;
  form: HolidayInput = this.blankForm();

  constructor() {
    addIcons({ addOutline, createOutline, trashOutline, sunnyOutline });
  }

  ngOnInit(): void {
    this.load();
  }

  private blankForm(): HolidayInput {
    return { date: '', name: '', isPaid: true };
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.holidaySvc.getByYear(this.year).subscribe({
      next: (h) => {
        this.holidays.set(h);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load holidays. Is the backend running?');
        this.loading.set(false);
      },
    });
  }

  openNew(): void {
    this.editingId = null;
    this.form = this.blankForm();
    this.form.date = `${this.year}-01-01`;
    this.modalOpen.set(true);
  }

  openEdit(h: Holiday): void {
    this.editingId = h.id;
    this.form = { date: h.date, name: h.name, isPaid: h.isPaid };
    this.modalOpen.set(true);
  }

  closeModal(): void {
    this.modalOpen.set(false);
  }

  save(): void {
    if (!this.form.date || !this.form.name.trim()) {
      this.toast('Date and Name are required.', 'warning');
      return;
    }
    this.saving.set(true);
    const obs =
      this.editingId == null ? this.holidaySvc.create(this.form) : this.holidaySvc.update(this.editingId, this.form);
    obs.subscribe({
      next: () => {
        this.saving.set(false);
        this.modalOpen.set(false);
        this.toast('Saved.', 'success');
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.toast('Failed to save.', 'danger');
      },
    });
  }

  async confirmDelete(h: Holiday): Promise<void> {
    const a = await this.alertCtrl.create({
      header: 'Delete holiday?',
      message: `${h.name} (${h.date})`,
      buttons: [
        { text: 'Cancel', role: 'cancel' },
        {
          text: 'Delete',
          role: 'destructive',
          handler: () =>
            this.holidaySvc.delete(h.id).subscribe({
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

  togglePaid(h: Holiday): void {
    this.holidaySvc.update(h.id, { date: h.date, name: h.name, isPaid: !h.isPaid }).subscribe({
      next: () => this.load(),
      error: () => this.toast('Failed to update.', 'danger'),
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2000, position: 'top' });
    await t.present();
  }
}
