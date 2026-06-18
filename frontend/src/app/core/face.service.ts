import { Injectable } from '@angular/core';
import * as faceapi from '@vladmandic/face-api';

// Browser-based face recognition using @vladmandic/face-api (face-api.js fork).
// Loads 3 nets (tinyFaceDetector, faceLandmark68Net, faceRecognitionNet) from
// /assets/models, manages the webcam, and produces a 128-d face descriptor that
// the .NET backend matches by Euclidean distance.
@Injectable({ providedIn: 'root' })
export class FaceService {
  private readonly modelUrl = '/assets/models';
  private loadPromise: Promise<void> | null = null;
  private stream: MediaStream | null = null;

  // Detection options reused across calls. tinyFaceDetector = fast + small models.
  // inputSize 224 (down from 320) is markedly faster on phones and still accurate
  // for a close-up kiosk face. Must be a multiple of 32.
  private readonly detectorOptions = new faceapi.TinyFaceDetectorOptions({
    inputSize: 224,
    scoreThreshold: 0.5,
  });

  // Reject a promise if it doesn't settle within `ms` — guards against any
  // step (model fetch / camera) hanging forever and freezing the UI spinner.
  private withTimeout<T>(p: Promise<T>, ms: number, label: string): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`${label} timed out after ${ms / 1000}s`)), ms);
      p.then(
        (v) => {
          clearTimeout(timer);
          resolve(v);
        },
        (e) => {
          clearTimeout(timer);
          reject(e);
        }
      );
    });
  }

  // Load the 3 nets once. Idempotent — the promise is cached so concurrent/repeat
  // callers share a single load. Times out so a stalled fetch can't hang the UI.
  loadModels(): Promise<void> {
    if (!this.loadPromise) {
      this.loadPromise = this.withTimeout(
        (async () => {
          // Prefer the GPU (WebGL) backend. On some mobile WebViews face-api can
          // silently fall back to the CPU backend, which is many times slower.
          // (face-api re-exports tfjs as `tf`; its bundled types omit these fns.)
          const tf = faceapi.tf as unknown as {
            setBackend(name: string): Promise<boolean>;
            ready(): Promise<void>;
          };
          try {
            await tf.setBackend('webgl');
            await tf.ready();
          } catch {
            /* keep whatever backend is available */
          }
          await faceapi.nets.tinyFaceDetector.loadFromUri(this.modelUrl);
          await faceapi.nets.faceLandmark68Net.loadFromUri(this.modelUrl);
          await faceapi.nets.faceRecognitionNet.loadFromUri(this.modelUrl);
          // Warm up: run one throwaway inference so the FIRST real scan doesn't
          // pay the one-time WebGL shader-compile cost (a ~1-2s UI freeze).
          await this.warmUp();
        })(),
        30000,
        'Loading face models'
      ).catch((err) => {
        // Reset so a later retry can attempt loading again.
        this.loadPromise = null;
        throw err;
      });
    }
    return this.loadPromise;
  }

  // One throwaway detection on a blank canvas so the expensive WebGL shader
  // compilation happens during loading, not on the user's first scan.
  private async warmUp(): Promise<void> {
    try {
      const c = document.createElement('canvas');
      c.width = 224;
      c.height = 224;
      await faceapi
        .detectSingleFace(c, this.detectorOptions)
        .withFaceLandmarks()
        .withFaceDescriptor();
    } catch {
      /* warm-up is best-effort */
    }
  }

  get modelsReady(): boolean {
    return (
      faceapi.nets.tinyFaceDetector.isLoaded &&
      faceapi.nets.faceLandmark68Net.isLoaded &&
      faceapi.nets.faceRecognitionNet.isLoaded
    );
  }

  // Start the front ("user") camera and pipe it into the given <video>.
  // Throws (with a friendly message) on permission denial / no device / timeout
  // so callers can surface it. Does NOT depend on the AI models being loaded.
  async startCamera(video: HTMLVideoElement): Promise<void> {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('Camera not available. Open the app over https or http://localhost.');
    }
    this.stopCamera();
    try {
      this.stream = await this.withTimeout(
        // A modest 640x480 front camera is plenty for face detection and lighter to
        // process/render than a full-res stream on a phone.
        navigator.mediaDevices.getUserMedia({
          video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
          audio: false,
        }),
        20000,
        'Camera'
      );
    } catch (e) {
      throw new Error(this.cameraErrorMessage(e));
    }
    video.srcObject = this.stream;
    video.muted = true;
    video.setAttribute('playsinline', '');
    video.setAttribute('autoplay', '');
    // Don't let a blocked autoplay hang us — frames still render via autoplay.
    try {
      await this.withTimeout(video.play(), 5000, 'Video playback');
    } catch {
      /* ignore — autoplay attribute keeps the preview live */
    }
  }

  // Turn a getUserMedia error into something a user can act on.
  private cameraErrorMessage(e: unknown): string {
    const name = (e as { name?: string })?.name ?? '';
    switch (name) {
      case 'NotAllowedError':
      case 'SecurityError':
        return 'Camera permission denied. Allow camera access in the browser and retry.';
      case 'NotFoundError':
      case 'DevicesNotFoundError':
        return 'No camera found on this device.';
      case 'NotReadableError':
      case 'TrackStartError':
        return 'Camera is in use by another app. Close it and retry.';
      default:
        return (e as Error)?.message || 'Could not start the camera.';
    }
  }

  stopCamera(): void {
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
  }

  // Detect a single face in the input and return its 128-d descriptor as a plain
  // number[], or null if no face is found. Loads models first if needed.
  async getDescriptor(input: HTMLVideoElement | HTMLImageElement): Promise<number[] | null> {
    await this.loadModels();
    const detection = await faceapi
      .detectSingleFace(input, this.detectorOptions)
      .withFaceLandmarks()
      .withFaceDescriptor();
    if (!detection) {
      return null;
    }
    return Array.from(detection.descriptor);
  }

  // Like getDescriptor(), but also returns the Eye Aspect Ratio (EAR) computed
  // from the 68 facial landmarks — used for the experimental blink/liveness check.
  // Returns null if no face is found.
  async detectFaceState(
    video: HTMLVideoElement
  ): Promise<{ descriptor: number[]; ear: number } | null> {
    await this.loadModels();
    const detection = await faceapi
      .detectSingleFace(video, this.detectorOptions)
      .withFaceLandmarks()
      .withFaceDescriptor();
    if (!detection) {
      return null;
    }
    const landmarks = detection.landmarks;
    // Each eye = 6 points: indices 36–41 (left) and 42–47 (right).
    const leftEar = this.eyeAspectRatio(landmarks.getLeftEye());
    const rightEar = this.eyeAspectRatio(landmarks.getRightEye());
    const ear = (leftEar + rightEar) / 2;
    return { descriptor: Array.from(detection.descriptor), ear };
  }

  // Eye Aspect Ratio for a single 6-point eye:
  // EAR = (||p2-p6|| + ||p3-p5||) / (2 * ||p1-p4||).
  // Points come in order p1..p6 from getLeftEye()/getRightEye().
  private eyeAspectRatio(eye: { x: number; y: number }[]): number {
    if (eye.length < 6) return 1; // treat as "open" if landmarks are missing
    const dist = (a: { x: number; y: number }, b: { x: number; y: number }): number =>
      Math.hypot(a.x - b.x, a.y - b.y);
    const vertical = dist(eye[1], eye[5]) + dist(eye[2], eye[4]);
    const horizontal = 2 * dist(eye[0], eye[3]);
    return horizontal === 0 ? 1 : vertical / horizontal;
  }
}
