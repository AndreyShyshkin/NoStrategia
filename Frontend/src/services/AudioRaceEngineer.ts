import { AudioPriority } from "../types/telemetry";

export interface AudioMessage {
  id: string;
  priority: AudioPriority;
  text: string;
  category?: string;
  timestamp: number;
}

export interface AudioEngineerOptions {
  enabled?: boolean;
  volume?: number;
  rate?: number;
  pitch?: number;
  lang?: string;
  minPriority?: AudioPriority;
  radioBeep?: boolean;
  onSpoken?: (msg: AudioMessage) => void;
  onStateChange?: (speaking: boolean, currentMsg: AudioMessage | null) => void;
}

export class AudioRaceEngineer {
  private queue: AudioMessage[] = [];
  private isSpeaking: boolean = false;
  private currentMessage: AudioMessage | null = null;
  private lastSpokenMap: Map<string, number> = new Map();
  private audioCtx: AudioContext | null = null;

  public enabled: boolean = true;
  public volume: number = 1.0;
  public rate: number = 1.05;
  public pitch: number = 0.95;
  public lang: string = "en-US";
  public minPriority: AudioPriority = AudioPriority.P4_STATUS;
  public radioBeep: boolean = true;
  private onSpoken?: (msg: AudioMessage) => void;
  private onStateChange?: (speaking: boolean, currentMsg: AudioMessage | null) => void;

  constructor(options: AudioEngineerOptions = {}) {
    if (options.enabled !== undefined) this.enabled = options.enabled;
    if (options.volume !== undefined) this.volume = options.volume;
    if (options.rate !== undefined) this.rate = options.rate;
    if (options.pitch !== undefined) this.pitch = options.pitch;
    if (options.lang !== undefined) this.lang = options.lang;
    if (options.minPriority !== undefined) this.minPriority = options.minPriority;
    if (options.radioBeep !== undefined) this.radioBeep = options.radioBeep;
    this.onSpoken = options.onSpoken;
    this.onStateChange = options.onStateChange;
  }

  /**
   * Post a tactical/telemetry callout to the engineer queue
   */
  public postCallout(text: string, priority: AudioPriority = AudioPriority.P3_TELEMETRY, category?: string) {
    if (!this.enabled) return;
    if (priority > this.minPriority) return; // filter out lower priorities if configured

    const now = Date.now();

    // Deduplication check: do not repeat exact phrases within 40 seconds unless P1
    const lastSpoken = this.lastSpokenMap.get(text) || 0;
    if (priority > AudioPriority.P1_CRITICAL && now - lastSpoken < 40000) {
      return;
    }

    const message: AudioMessage = {
      id: `audio-${Math.random().toString(36).substring(2, 9)}`,
      priority,
      text,
      category,
      timestamp: now,
    };

    if (priority === AudioPriority.P1_CRITICAL) {
      // P1 Critical alert immediately cancels any ongoing speech and clears lesser alerts
      if (typeof window !== "undefined" && window.speechSynthesis) {
        window.speechSynthesis.cancel();
      }
      this.isSpeaking = false;
      // Filter out low priority P3/P4 items from pending queue, keep only pending P1/P2
      this.queue = this.queue.filter((m) => m.priority <= AudioPriority.P2_STRATEGY);
      this.queue.unshift(message);
      this.speakNext();
      return;
    }

    // P4 status messages are dropped if queue is already backed up (> 2 items)
    if (priority === AudioPriority.P4_STATUS && this.queue.length >= 2) {
      return;
    }

    // Insert into priority queue
    this.queue.push(message);
    this.queue.sort((a, b) => a.priority - b.priority);

    if (!this.isSpeaking) {
      this.speakNext();
    }
  }

  private speakNext() {
    if (typeof window === "undefined" || !window.speechSynthesis) {
      this.isSpeaking = false;
      return;
    }

    if (this.queue.length === 0) {
      this.isSpeaking = false;
      this.currentMessage = null;
      if (this.onStateChange) this.onStateChange(false, null);
      return;
    }

    this.isSpeaking = true;
    const current = this.queue.shift()!;
    this.currentMessage = current;
    this.lastSpokenMap.set(current.text, Date.now());

    if (this.onStateChange) this.onStateChange(true, current);

    // Optional short pit-radio static squelch/beep before speech
    if (this.radioBeep) {
      this.playRadioBeep();
    }

    const utterance = new SpeechSynthesisUtterance(current.text);
    utterance.volume = Math.max(0, Math.min(1, this.volume));
    utterance.rate = this.rate;
    utterance.pitch = this.pitch;
    utterance.lang = this.lang;

    // Pick a natural English or preferred voice if available
    const voices = window.speechSynthesis.getVoices();
    if (voices.length > 0) {
      const preferred = voices.find(
        (v) => (v.lang.startsWith("en") || v.lang.startsWith("uk")) && (v.name.includes("Natural") || v.name.includes("Google") || v.name.includes("Daniel") || v.name.includes("Samantha"))
      ) || voices.find((v) => v.lang.startsWith("en"));
      if (preferred) {
        utterance.voice = preferred;
      }
    }

    utterance.onend = () => {
      if (this.onSpoken) this.onSpoken(current);
      // Small pause between callouts
      setTimeout(() => {
        this.speakNext();
      }, 250);
    };

    utterance.onerror = (e) => {
      // If cancelled by another P1 callout, it's normal
      if (this.onSpoken) this.onSpoken(current);
      this.speakNext();
    };

    window.speechSynthesis.speak(utterance);
  }

  /**
   * Authentic radio chirp using Web Audio API
   */
  private playRadioBeep() {
    try {
      if (typeof window === "undefined") return;
      const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
      if (!AudioCtx) return;

      if (!this.audioCtx) {
        this.audioCtx = new AudioCtx();
      }

      if (this.audioCtx.state === "suspended") {
        this.audioCtx.resume();
      }

      const osc = this.audioCtx.createOscillator();
      const gain = this.audioCtx.createGain();

      osc.type = "sine";
      osc.frequency.setValueAtTime(1400, this.audioCtx.currentTime);
      osc.frequency.exponentialRampToValueAtTime(1800, this.audioCtx.currentTime + 0.04);

      gain.gain.setValueAtTime(0.08 * this.volume, this.audioCtx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.001, this.audioCtx.currentTime + 0.05);

      osc.connect(gain);
      gain.connect(this.audioCtx.destination);

      osc.start();
      osc.stop(this.audioCtx.currentTime + 0.05);
    } catch {
      // AudioContext might be blocked before first user gesture
    }
  }

  public cancel() {
    if (typeof window !== "undefined" && window.speechSynthesis) {
      window.speechSynthesis.cancel();
    }
    this.queue = [];
    this.isSpeaking = false;
    this.currentMessage = null;
    if (this.onStateChange) this.onStateChange(false, null);
  }

  public getQueueLength(): number {
    return this.queue.length;
  }

  public getSpeakingState(): { isSpeaking: boolean; currentMessage: AudioMessage | null } {
    return { isSpeaking: this.isSpeaking, currentMessage: this.currentMessage };
  }
}
