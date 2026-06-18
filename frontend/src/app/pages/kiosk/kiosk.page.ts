import { Component, OnDestroy, ViewChild, ElementRef, inject, signal } from '@angular/core';
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
  IonCard,
  IonCardContent,
  IonItem,
  IonInput,
  IonButton,
  IonIcon,
  IonSpinner,
  IonText,
  IonBadge,
  IonToggle,
} from '@ionic/angular/standalone';
import { Router } from '@angular/router';
import { addIcons } from 'ionicons';
import {
  scanOutline,
  cameraOutline,
  logInOutline,
  logOutOutline,
  checkmarkCircle,
  personRemoveOutline,
  personAddOutline,
  eyeOutline,
} from 'ionicons/icons';
import { AttendanceService } from '../../core/attendance.service';
import { FaceService } from '../../core/face.service';
import { SettingsService } from '../../core/settings.service';
import { SpeechService } from '../../core/speech.service';
import { AuthService } from '../../core/auth.service';
import { PunchResult } from '../../core/models';
import { fmtMinutes, fmtTime } from '../../core/util';

@Component({
  selector: 'app-kiosk',
  standalone: true,
  templateUrl: './kiosk.page.html',
  styleUrls: ['./kiosk.page.scss'],
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
    IonCard,
    IonCardContent,
    IonItem,
    IonInput,
    IonButton,
    IonIcon,
    IonSpinner,
    IonText,
    IonBadge,
    IonToggle,
  ],
})
export class KioskPage implements OnDestroy {
  @ViewChild('video') videoRef?: ElementRef<HTMLVideoElement>;
  @ViewChild('canvas') canvasRef?: ElementRef<HTMLCanvasElement>;

  private attendance = inject(AttendanceService);
  private face = inject(FaceService);
  private settingsSvc = inject(SettingsService);
  private speech = inject(SpeechService);
  private auth = inject(AuthService);
  private router = inject(Router);
  private readonly deviceId = 'kiosk-web-1';

  code = '';
  loading = signal(false);
  error = signal<string | null>(null);
  result = signal<PunchResult | null>(null);
  cameraOn = signal(false);

  // Prominent "NON-EMPLOYEE" state — set when a FACE scan returns no match (404).
  // No punch is recorded; it auto-clears after the cooldown.
  nonEmployee = signal(false);

  // Face-recognition state.
  modelsLoading = signal(false);
  modelsReady = signal(false);
  scanning = signal(false); // a face detection is currently running

  // ===== Detection-time meter (for the Ionic-vs-Flutter speed comparison) =====
  detectMs = signal(0);
  detectAvg = signal(0);
  private detectTimes: number[] = [];
  private recordDetect(ms: number): void {
    const v = Math.round(ms);
    this.detectMs.set(v);
    this.detectTimes.push(v);
    if (this.detectTimes.length > 30) this.detectTimes.shift();
    this.detectAvg.set(Math.round(this.detectTimes.reduce((a, b) => a + b, 0) / this.detectTimes.length));
  }
  autoScan = signal(true); // continuous auto-detect mode (no button needed)
  cooldown = signal(false); // brief pause after a punch so one person isn't re-punched

  // Liveness (blink) — only enforced when settings.requireLiveness is true.
  requireLiveness = signal(false); // loaded from GET /settings on enter (default false)
  voiceEnabled = signal(true); // speak greeting on punch (admin Settings toggle)
  awaitingBlink = signal(false); // true while we wait for the user to blink

  // Blink state machine: we need EAR to dip below EAR_CLOSED (eyes closed) and
  // then rise back above EAR_OPEN (eyes open) within a short window to confirm
  // a *live* blink. Lenient/experimental thresholds.
  private readonly EAR_CLOSED = 0.2; // below this = eyes closed
  private readonly EAR_OPEN = 0.25; // back above this = eyes open again
  private readonly BLINK_WINDOW = 4000; // ms allowed to complete a blink
  private sawClose = false; // saw eyes go closed
  private blinkStartedAt = 0; // when we started waiting for the current blink
  private latestDescriptor: number[] | null = null; // freshest descriptor for the blinker

  private destroyed = false;
  private active = false; // true only while THIS page is the visible one (Ionic lifecycle)
  private scanTimer: ReturnType<typeof setTimeout> | null = null;
  private cooldownTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly SCAN_INTERVAL = 700; // ms between auto detection attempts
  private readonly BLINK_INTERVAL = 250; // faster sampling while waiting for a blink
  private readonly OK_COOLDOWN = 6000; // pause after a successful punch
  private readonly RETRY_COOLDOWN = 3000; // pause after a non-match / error

  constructor() {
    addIcons({
      scanOutline,
      cameraOutline,
      logInOutline,
      logOutOutline,
      checkmarkCircle,
      personRemoveOutline,
      personAddOutline,
      eyeOutline,
    });
  }

  // Ionic fires this every time the page becomes visible (incl. first load). We
  // (re)start the camera + scanning here so it only runs while on this page.
  async ionViewDidEnter(): Promise<void> {
    this.active = true;
    await this.startCamera();
    await this.ensureModels();
    this.loadSettings();
    this.scheduleScan(800);
  }

  // Leaving the page (navigating away / page hidden): STOP camera + scanning so it
  // doesn't keep running (and announcing) on other pages.
  ionViewWillLeave(): void {
    this.active = false;
    this.clearTimers();
    this.face.stopCamera();
    this.cameraOn.set(false);
    this.scanning.set(false);
  }

  // Load global settings to know whether liveness is required. Tolerates the
  // backend being down — defaults requireLiveness to false.
  private loadSettings(): void {
    this.settingsSvc.get().subscribe({
      next: (s) => {
        this.requireLiveness.set(!!s.requireLiveness);
        this.voiceEnabled.set(s.voiceEnabled !== false); // default ON
      },
      error: () => this.requireLiveness.set(false),
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.clearTimers();
    this.face.stopCamera();
    this.cameraOn.set(false);
  }

  private clearTimers(): void {
    if (this.scanTimer) {
      clearTimeout(this.scanTimer);
      this.scanTimer = null;
    }
    if (this.cooldownTimer) {
      clearTimeout(this.cooldownTimer);
      this.cooldownTimer = null;
    }
  }

  // Whether someone is logged into the admin app (vs the public door scanner).
  isLoggedIn(): boolean {
    return this.auth.isLoggedIn();
  }

  // Login / Sign Up are reached ONLY by tapping these (no auto-redirect).
  goToLogin(): void {
    void this.router.navigate(['/login']);
  }

  goToSignup(): void {
    void this.router.navigate(['/signup']);
  }

  // Toggle continuous auto-scan on/off (manual SCAN FACE button appears when off).
  toggleAutoScan(): void {
    this.autoScan.set(!this.autoScan());
    if (this.autoScan()) this.scheduleScan(200);
  }

  fmt(min: number): string {
    return fmtMinutes(min);
  }

  fmtTime(iso: string | null | undefined): string {
    return fmtTime(iso);
  }

  async startCamera(): Promise<void> {
    const video = this.videoRef?.nativeElement;
    if (!video) {
      this.cameraOn.set(false);
      return;
    }
    try {
      await this.face.startCamera(video);
      this.cameraOn.set(true);
      this.error.set(null);
    } catch (e) {
      // Camera permission denied / no device — code-based punch still works.
      this.cameraOn.set(false);
      this.error.set(
        (e instanceof Error ? e.message : 'Camera unavailable.') + ' You can still punch by CODE.'
      );
    }
  }

  // Load the face-recognition models (idempotent). Shows a small loading state.
  private async ensureModels(): Promise<void> {
    if (this.modelsReady() || this.modelsLoading()) return;
    this.modelsLoading.set(true);
    try {
      await this.face.loadModels();
      this.modelsReady.set(true);
    } catch {
      this.error.set('Could not load face models. You can still punch by CODE.');
    } finally {
      this.modelsLoading.set(false);
    }
  }

  // ===== Continuous auto-scan loop =====
  // Schedules the next detection attempt. Self-reschedules so only one runs at a time.
  private scheduleScan(delay: number = this.SCAN_INTERVAL): void {
    if (this.destroyed || !this.active) return; // never scan while off this page
    if (this.scanTimer) clearTimeout(this.scanTimer);
    this.scanTimer = setTimeout(() => void this.autoTick(), delay);
  }

  // One detection tick. When liveness is OFF this just punches on first detection.
  // When liveness is ON it runs a small blink state machine and only punches
  // after a confirmed live blink.
  private async autoTick(): Promise<void> {
    if (this.destroyed || !this.active || !this.autoScan()) return;
    // Not ready, busy, or cooling down → just try again shortly.
    if (!this.cameraOn() || !this.modelsReady() || this.scanning() || this.loading() || this.cooldown()) {
      this.resetBlink();
      this.scheduleScan();
      return;
    }
    const video = this.videoRef?.nativeElement;
    if (!video) {
      this.scheduleScan();
      return;
    }
    this.scanning.set(true);
    try {
      if (this.requireLiveness()) {
        await this.blinkTick(video);
      } else {
        // Instant behavior: punch on first detection. (Timed for the speed comparison.)
        const t0 = performance.now();
        const descriptor = await this.face.getDescriptor(video);
        this.recordDetect(performance.now() - t0);
        this.scanning.set(false);
        if (descriptor) {
          await this.punchWithDescriptor(descriptor); // sets a cooldown internally
        }
        this.scheduleScan();
        return;
      }
    } catch {
      // ignore detection errors — just keep looping
    }
    this.scanning.set(false);
    // While waiting for a blink, sample a bit faster for a responsive feel.
    this.scheduleScan(this.awaitingBlink() ? this.BLINK_INTERVAL : this.SCAN_INTERVAL);
  }

  // Experimental blink/liveness check. Require EAR to dip below EAR_CLOSED
  // (eyes closed) and then rise back above EAR_OPEN (eyes open) within
  // BLINK_WINDOW ms; only then punch using the latest descriptor.
  private async blinkTick(video: HTMLVideoElement): Promise<void> {
    const state = await this.face.detectFaceState(video);
    if (!state) {
      // No face in front — reset so we don't carry a stale half-blink.
      this.resetBlink();
      return;
    }
    this.latestDescriptor = state.descriptor;
    const now = Date.now();
    if (!this.awaitingBlink()) {
      // First detection of this person — start waiting for a blink.
      this.awaitingBlink.set(true);
      this.blinkStartedAt = now;
      this.sawClose = false;
    }
    // Time out the blink window and restart so a slow person can retry.
    if (now - this.blinkStartedAt > this.BLINK_WINDOW) {
      this.blinkStartedAt = now;
      this.sawClose = false;
    }
    if (state.ear < this.EAR_CLOSED) {
      this.sawClose = true; // eyes closed observed
    } else if (this.sawClose && state.ear > this.EAR_OPEN) {
      // Eyes re-opened after closing → confirmed live blink. Punch now.
      const descriptor = this.latestDescriptor;
      this.resetBlink();
      this.scanning.set(false);
      if (descriptor) {
        await this.punchWithDescriptor(descriptor);
      }
    }
  }

  private resetBlink(): void {
    this.awaitingBlink.set(false);
    this.sawClose = false;
    this.blinkStartedAt = 0;
    this.latestDescriptor = null;
  }

  // Manual trigger — used only when auto-scan is turned off.
  async scanFace(): Promise<void> {
    const video = this.videoRef?.nativeElement;
    if (!video || !this.cameraOn()) {
      this.error.set('Camera is not available. Use the employee CODE instead.');
      return;
    }
    await this.ensureModels();
    if (!this.modelsReady()) return;
    this.scanning.set(true);
    let descriptor: number[] | null = null;
    try {
      descriptor = await this.face.getDescriptor(video);
    } catch {
      descriptor = null;
    }
    this.scanning.set(false);
    if (!descriptor) {
      this.error.set('No face detected. Look at the camera and try again.');
      return;
    }
    await this.punchWithDescriptor(descriptor);
  }

  // Send the descriptor to the backend (nearest-match) and show the result.
  // On a non-match (404) we show a PROMINENT "NON-EMPLOYEE" card — NO punch
  // is recorded. It auto-clears after the cooldown.
  private punchWithDescriptor(descriptor: number[]): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    this.nonEmployee.set(false);
    return new Promise<void>((resolve) => {
      this.attendance
        .punch({ faceDescriptor: descriptor, deviceId: this.deviceId, source: 'Face' })
        .subscribe({
          next: (res) => {
            this.result.set(res);
            this.error.set(null);
            this.nonEmployee.set(false);
            this.loading.set(false);
            // Fill the code box with the matched employee's real ID/code.
            this.code = res.punch.employeeCode ?? '';
            // Greet the employee aloud by name on a successful IN/OUT.
            if (this.voiceEnabled()) this.speech.announcePunch(res.punch.employeeName, res.punch.direction);
            this.startCooldown(this.OK_COOLDOWN);
            resolve();
          },
          error: (err) => {
            this.result.set(null);
            this.loading.set(false);
            if (err?.status === 404) {
              // Unknown face → show NON-EMPLOYEE briefly. NO auto-redirect — the
              // always-visible Login / Sign Up buttons let them choose manually.
              this.nonEmployee.set(true);
              this.error.set(null);
              this.startCooldown(this.RETRY_COOLDOWN);
            } else {
              this.nonEmployee.set(false);
              this.error.set('Punch failed. Please try again.');
              this.startCooldown(this.RETRY_COOLDOWN);
            }
            resolve();
          },
        });
    });
  }

  // Pause detection briefly after a punch so the same person standing in front
  // isn't punched again and again (which would wrongly toggle IN/OUT).
  // When the cooldown ends we clear the NON-EMPLOYEE card and scanning resumes.
  private startCooldown(ms: number): void {
    this.cooldown.set(true);
    if (this.cooldownTimer) clearTimeout(this.cooldownTimer);
    this.cooldownTimer = setTimeout(() => {
      this.cooldown.set(false);
      this.nonEmployee.set(false);
    }, ms);
  }

  // Code-based punch — fully wired per contract (fallback).
  punchByCode(): void {
    const code = this.code.trim();
    if (!code) {
      this.error.set('Please enter the employee CODE first.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.result.set(null);
    this.nonEmployee.set(false);
    this.attendance.punch({ employeeCode: code, deviceId: this.deviceId, source: 'Code' }).subscribe({
      next: (res) => {
        this.result.set(res);
        this.loading.set(false);
        this.code = '';
        // Greet the employee aloud by name on a successful IN/OUT.
        this.speech.announcePunch(res.punch.employeeName, res.punch.direction);
        // Don't let auto-scan immediately re-punch this person by face.
        this.startCooldown(this.OK_COOLDOWN);
      },
      error: (err) => {
        const msg =
          err?.status === 404
            ? 'No employee found for this code.'
            : 'Punch failed. Check the backend at http://localhost:5080.';
        this.error.set(msg);
        this.loading.set(false);
      },
    });
  }
}
