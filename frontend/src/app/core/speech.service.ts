import { Injectable } from '@angular/core';
import { Capacitor } from '@capacitor/core';
import { TextToSpeech } from '@capacitor-community/text-to-speech';

// Speaks short confirmations aloud at the kiosk.
// - On a NATIVE app (Android/iOS): uses the Capacitor TextToSpeech plugin (the
//   phone's built-in TTS engine) — reliable, unlike Web Speech in a WebView.
// - In a browser: uses the Web Speech API (SpeechSynthesis).
@Injectable({ providedIn: 'root' })
export class SpeechService {
  private get synth(): SpeechSynthesis | null {
    return typeof window !== 'undefined' && 'speechSynthesis' in window ? window.speechSynthesis : null;
  }

  // Speak the given text. No-ops silently if unsupported.
  speak(text: string): void {
    if (!text) return;

    if (Capacitor.isNativePlatform()) {
      // Native phone TTS (works inside the APK WebView).
      TextToSpeech.stop().catch(() => undefined);
      TextToSpeech.speak({
        text,
        lang: 'en-US',
        rate: 1.0,
        pitch: 1.0,
        volume: 1.0,
        category: 'ambient',
      }).catch(() => undefined);
      return;
    }

    // Browser fallback.
    const synth = this.synth;
    if (!synth) return;
    try {
      synth.cancel();
      const u = new SpeechSynthesisUtterance(text);
      u.lang = 'en-US';
      u.rate = 1;
      u.pitch = 1;
      u.volume = 1;
      synth.speak(u);
    } catch {
      /* speech unavailable - ignore */
    }
  }

  // Greet on a punch, e.g. "Thank you Rajanish Maury. Checked in."
  announcePunch(name: string, direction: 'IN' | 'OUT'): void {
    const action = direction === 'IN' ? 'Checked in' : 'Checked out';
    this.speak(`Thank you ${name}. ${action}.`);
  }
}
