import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import {
  IonApp,
  IonRouterOutlet,
  IonSplitPane,
  IonMenu,
  IonContent,
  IonList,
  IonListHeader,
  IonNote,
  IonMenuToggle,
  IonItem,
  IonIcon,
  IonLabel,
  IonText,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  speedometerOutline,
  scanOutline,
  peopleOutline,
  cardOutline,
  calendarOutline,
  documentTextOutline,
  sunnyOutline,
  airplaneOutline,
  cashOutline,
  settingsOutline,
  shieldCheckmarkOutline,
  logOutOutline,
  personCircleOutline,
} from 'ionicons/icons';
import { AuthService } from './core/auth.service';

interface AppPage {
  title: string;
  url: string;
  icon: string;
  key: string; // RBAC page key — must match CONTRACT.md page keys.
}

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  imports: [
    RouterLink,
    RouterLinkActive,
    IonApp,
    IonRouterOutlet,
    IonSplitPane,
    IonMenu,
    IonContent,
    IonList,
    IonListHeader,
    IonNote,
    IonMenuToggle,
    IonItem,
    IonIcon,
    IonLabel,
    IonText,
  ],
})
export class AppComponent {
  readonly auth = inject(AuthService);

  // All menu items with their RBAC page keys.
  private readonly allPages: AppPage[] = [
    { title: 'Dashboard', url: '/dashboard', icon: 'speedometer-outline', key: 'dashboard' },
    { title: 'Attendance', url: '/kiosk', icon: 'scan-outline', key: 'kiosk' },
    { title: 'Employees', url: '/employees', icon: 'people-outline', key: 'employees' },
    { title: 'ID Card', url: '/idcard', icon: 'card-outline', key: 'employees' },
    { title: 'Daily Attendance', url: '/daily', icon: 'calendar-outline', key: 'daily' },
    { title: 'Monthly Report', url: '/report', icon: 'document-text-outline', key: 'report' },
    { title: 'Holidays', url: '/holidays', icon: 'sunny-outline', key: 'holidays' },
    { title: 'Leaves', url: '/leaves', icon: 'airplane-outline', key: 'leaves' },
    { title: 'Salary (Admin)', url: '/salary', icon: 'cash-outline', key: 'salary' },
    { title: 'Settings (Admin)', url: '/settings', icon: 'settings-outline', key: 'settings' },
    { title: 'Permissions', url: '/permissions', icon: 'shield-checkmark-outline', key: 'permissions' },
  ];

  // Only show pages the logged-in user is allowed to open.
  readonly appPages = computed<AppPage[]>(() => {
    if (!this.auth.currentUser()) return [];
    return this.allPages.filter((p) => this.auth.hasPage(p.key));
  });

  constructor() {
    addIcons({
      speedometerOutline,
      scanOutline,
      peopleOutline,
      cardOutline,
      calendarOutline,
      documentTextOutline,
      sunnyOutline,
      airplaneOutline,
      cashOutline,
      settingsOutline,
      shieldCheckmarkOutline,
      logOutOutline,
      personCircleOutline,
    });
  }

  logout(): void {
    this.auth.logout();
  }
}
