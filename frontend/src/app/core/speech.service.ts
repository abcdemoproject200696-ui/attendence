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
      // Native phone TTS. High pitch -> lighter / female-sounding voice.
      TextToSpeech.stop().catch(() => undefined);
      TextToSpeech.speak({
        text,
        lang: 'en-US',
        rate: 1.0,
        pitch: 1.5,
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
      u.pitch = 1.5; // higher pitch = lighter / female-sounding
      u.volume = 1;
      const female = this.pickFemaleVoice(synth);
      if (female) u.voice = female;
      synth.speak(u);
    } catch {
      /* speech unavailable - ignore */
    }
  }

  // Try to pick a female English voice in the browser (best-effort).
  private pickFemaleVoice(synth: SpeechSynthesis): SpeechSynthesisVoice | null {
    const voices = synth.getVoices();
    if (!voices.length) return null;
    const en = voices.filter((v) => v.lang.toLowerCase().startsWith('en'));
    const byName = (kw: string) => en.find((v) => v.name.toLowerCase().includes(kw));
    return (
      byName('female') ||
      byName('zira') ||
      byName('samantha') ||
      byName('google uk english female') ||
      byName('aria') ||
      en[0] ||
      null
    );
  }

  // Greet on a punch: IN -> "Welcome to TA, {name}", OUT -> "Thank you {name}".
  announcePunch(name: string, direction: 'IN' | 'OUT'): void {
    this.speak(direction === 'IN' ? `Welcome to TA, ${name}` : `Thank you ${name}`);
  }
}
