import { Component, ViewChild, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  IonContent,
  IonCard,
  IonCardHeader,
  IonCardTitle,
  IonCardSubtitle,
  IonCardContent,
  IonButton,
  IonIcon,
  IonSpinner,
  ToastController,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { personAddOutline, logInOutline } from 'ionicons/icons';
import { EmployeeService } from '../../core/employee.service';
import { EmployeeInput } from '../../core/models';
import { EmployeeFormComponent } from '../employees/employee-form.component';

// Public self-service signup. Reuses the shared employee form (password REQUIRED)
// and, on success, sends the user to /login to sign in with their new credentials.
@Component({
  selector: 'app-signup',
  standalone: true,
  templateUrl: './signup.page.html',
  styleUrls: ['./signup.page.scss'],
  imports: [
    RouterLink,
    IonContent,
    IonCard,
    IonCardHeader,
    IonCardTitle,
    IonCardSubtitle,
    IonCardContent,
    IonButton,
    IonIcon,
    IonSpinner,
    EmployeeFormComponent,
  ],
})
export class SignupPage {
  @ViewChild('employeeForm') employeeForm?: EmployeeFormComponent;

  private employeeSvc = inject(EmployeeService);
  private router = inject(Router);
  private toastCtrl = inject(ToastController);

  saving = signal(false);

  constructor() {
    addIcons({ personAddOutline, logInOutline });
  }

  // Save button -> validate + emit payload via the shared form's submit().
  triggerSave(): void {
    this.employeeForm?.submit();
  }

  // Receives the validated EmployeeInput and creates the account.
  onSubmit(payload: EmployeeInput): void {
    this.saving.set(true);
    this.employeeSvc.signup(payload).subscribe({
      next: () => {
        this.saving.set(false);
        this.employeeForm?.stopCamera();
        this.toast('Account created but INACTIVE — an admin must approve it before you can log in.', 'warning');
        void this.router.navigate(['/login']);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.toast(
          err.status === 409 || err.status === 400
            ? 'Could not create the account. The details may already be in use.'
            : 'Sign up failed. Is the backend running?',
          'danger',
        );
      },
    });
  }

  private async toast(message: string, color: string): Promise<void> {
    const t = await this.toastCtrl.create({ message, color, duration: 2500, position: 'top' });
    await t.present();
  }
}
