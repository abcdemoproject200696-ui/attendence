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
      // Native phone TTS. lang en-IN -> Indian English accent (if the device has it).
      TextToSpeech.stop().catch(() => undefined);
      TextToSpeech.speak({
        text,
        lang: 'en-IN',
        rate: 1.0,
        pitch: 1.4, // slightly higher -> lighter / female-sounding
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
      u.lang = 'en-IN'; // Indian English
      u.rate = 1;
      u.pitch = 1.4; // higher pitch = lighter / female-sounding
      u.volume = 1;
      const voice = this.pickIndianVoice(synth);
      if (voice) u.voice = voice;
      synth.speak(u);
    } catch {
      /* speech unavailable - ignore */
    }
  }

  // Prefer an Indian English voice (en-IN), ideally female; fall back to any English voice.
  private pickIndianVoice(synth: SpeechSynthesis): SpeechSynthesisVoice | null {
    const voices = synth.getVoices();
    if (!voices.length) return null;
    const byName = (kw: string) => voices.find((v) => v.name.toLowerCase().includes(kw));
    const indian = voices.filter(
      (v) => v.lang.toLowerCase() === 'en-in' || v.lang.toLowerCase() === 'hi-in',
    );
    const indianFemale = indian.find((v) => /female|heera|kalpana|swara|aditi/i.test(v.name));
    const en = voices.filter((v) => v.lang.toLowerCase().startsWith('en'));
    return (
      indianFemale ||          // Indian female (e.g. Microsoft Heera)
      byName('heera') ||       // en-IN female (Windows)
      byName('google english (india)') ||
      indian[0] ||             // any Indian voice (incl. Ravi male)
      en.find((v) => v.name.toLowerCase().includes('female')) ||
      en[0] ||
      null
    );
  }

  // Greet on a punch: IN -> "Permission granted, {name}", OUT -> "Thank you, {name}".
  announcePunch(name: string, direction: 'IN' | 'OUT'): void {
    this.speak(direction === 'IN' ? `Permission granted, ${name}` : `Thank you, ${name}`);
  }
}
