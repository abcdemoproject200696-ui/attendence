import { Injectable } from '@angular/core';

// Speaks short confirmations aloud at the kiosk using the browser's built-in
// Web Speech API (SpeechSynthesis) — free, offline, no backend. Used to greet
// an employee by name on a successful IN/OUT punch.
@Injectable({ providedIn: 'root' })
export class SpeechService {
  private get synth(): SpeechSynthesis | null {
    return typeof window !== 'undefined' && 'speechSynthesis' in window ? window.speechSynthesis : null;
  }

  // Speak the given text. Cancels anything already speaking so back-to-back
  // punches don't queue up. Silently no-ops if speech isn't supported.
  speak(text: string): void {
    const synth = this.synth;
    if (!synth || !text) return;
    try {
      synth.cancel();
      const u = new SpeechSynthesisUtterance(text);
      u.lang = 'en-US';
      u.rate = 1;
      u.pitch = 1;
      u.volume = 1;
      synth.speak(u);
    } catch {
      /* speech unavailable — ignore */
    }
  }

  // Greet on a punch, e.g. "Thank you Rajanish Maury. Checked in."
  announcePunch(name: string, direction: 'IN' | 'OUT'): void {
    const action = direction === 'IN' ? 'Checked in' : 'Checked out';
    this.speak(`Thank you ${name}. ${action}.`);
  }
}
