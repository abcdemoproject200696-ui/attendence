import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import {
  IonContent,
  IonCard,
  IonCardHeader,
  IonCardTitle,
  IonCardSubtitle,
  IonCardContent,
  IonItem,
  IonInput,
  IonButton,
  IonIcon,
  IonText,
  IonSpinner,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { logInOutline, lockClosedOutline, personOutline, eyeOutline, eyeOffOutline } from 'ionicons/icons';
import { AuthService } from '../../core/auth.service';

// Clean, responsive login form. Default route for logged-out users.
@Component({
  selector: 'app-login',
  standalone: true,
  templateUrl: './login.page.html',
  styleUrls: ['./login.page.scss'],
  imports: [
    FormsModule,
    RouterLink,
    IonContent,
    IonCard,
    IonCardHeader,
    IonCardTitle,
    IonCardSubtitle,
    IonCardContent,
    IonItem,
    IonInput,
    IonButton,
    IonIcon,
    IonText,
    IonSpinner,
  ],
})
export class LoginPage implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);

  code = '';
  password = '';
  loading = signal(false);
  error = signal<string | null>(null);
  showPassword = signal(false); // eye toggle for the password field

  constructor() {
    addIcons({ logInOutline, lockClosedOutline, personOutline, eyeOutline, eyeOffOutline });
  }

  ngOnInit(): void {
    // Already logged in? Skip straight to an allowed page.
    if (this.auth.isLoggedIn()) {
      this.goToAllowed();
    }
  }

  login(): void {
    if (!this.code.trim() || !this.password) {
      this.error.set('Please enter your employee code and password.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.auth.login(this.code.trim(), this.password).subscribe({
      next: () => {
        this.loading.set(false);
        this.goToAllowed();
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(
          err.status === 401
            ? 'Invalid code or password.'
            : 'Could not sign in. Is the backend running?',
        );
      },
    });
  }

  // Navigate to /dashboard if allowed, otherwise the first allowed page.
  private goToAllowed(): void {
    const target = this.auth.hasPage('dashboard') ? 'dashboard' : this.auth.firstAllowedPage();
    void this.router.navigate([target ? `/${target}` : '/login']);
  }
}
