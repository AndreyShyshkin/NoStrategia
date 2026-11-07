"use client";

import React, { useState, useEffect } from "react";
import { AudioRaceEngineer, AudioMessage } from "../../services/AudioRaceEngineer";
import { AudioPriority } from "../../types/telemetry";
import { Volume2, VolumeX, Mic, Radio, AlertOctagon, Zap, ShieldAlert, CheckCircle, Info } from "lucide-react";

interface AudioEngineerWidgetProps {
  audioEngineer: AudioRaceEngineer;
  recentCallouts: AudioMessage[];
  onTriggerCallout?: (text: string, priority: AudioPriority) => void;
  className?: string;
}

export const AudioEngineerWidget: React.FC<AudioEngineerWidgetProps> = ({
  audioEngineer,
  recentCallouts,
  onTriggerCallout,
  className = "",
}) => {
  const [isEnabled, setIsEnabled] = useState<boolean>(audioEngineer.enabled);
  const [volume, setVolume] = useState<number>(audioEngineer.volume);
  const [radioBeep, setRadioBeep] = useState<boolean>(audioEngineer.radioBeep);
  const [minPriority, setMinPriority] = useState<AudioPriority>(audioEngineer.minPriority);
  const [isSpeaking, setIsSpeaking] = useState<boolean>(false);
  const [currentMessage, setCurrentMessage] = useState<AudioMessage | null>(null);

  useEffect(() => {
    // Check speech synthesis support in browser
    if (typeof window !== "undefined" && !window.speechSynthesis) {
      console.warn("Web Speech API is not supported in this browser.");
    }
  }, []);

  const handleToggleMute = () => {
    const next = !isEnabled;
    setIsEnabled(next);
    audioEngineer.enabled = next;
    if (!next) {
      audioEngineer.cancel();
      setIsSpeaking(false);
      setCurrentMessage(null);
    }
  };

  const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = parseFloat(e.target.value);
    setVolume(val);
    audioEngineer.volume = val;
  };

  const handleToggleBeep = () => {
    const next = !radioBeep;
    setRadioBeep(next);
    audioEngineer.radioBeep = next;
  };

  const handlePriorityFilter = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const p = parseInt(e.target.value, 10) as AudioPriority;
    setMinPriority(p);
    audioEngineer.minPriority = p;
  };

  const triggerTestAlert = (text: string, priority: AudioPriority) => {
    if (onTriggerCallout) {
      onTriggerCallout(text, priority);
    } else {
      audioEngineer.postCallout(text, priority);
    }
  };

  const getPriorityBadge = (priority: AudioPriority) => {
    switch (priority) {
      case AudioPriority.P1_CRITICAL:
        return <span className="rounded bg-rose-500/20 px-1.5 py-0.5 text-[10px] font-bold text-rose-400 border border-rose-500/40">P1 CRITICAL</span>;
      case AudioPriority.P2_STRATEGY:
        return <span className="rounded bg-amber-500/20 px-1.5 py-0.5 text-[10px] font-bold text-amber-400 border border-amber-500/40">P2 STRATEGY</span>;
      case AudioPriority.P3_TELEMETRY:
        return <span className="rounded bg-cyan-500/20 px-1.5 py-0.5 text-[10px] font-bold text-cyan-400 border border-cyan-500/40">P3 TELEMETRY</span>;
      case AudioPriority.P4_STATUS:
        return <span className="rounded bg-emerald-500/20 px-1.5 py-0.5 text-[10px] font-bold text-emerald-400 border border-emerald-500/40">P4 STATUS</span>;
    }
  };

  return (
    <div className={`rounded-xl border border-gray-800 bg-[#0d1117] p-4 shadow-xl ${className}`}>
      {/* Widget Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-gray-800/80 pb-3">
        <div className="flex items-center gap-2.5">
          <div className={`flex h-8 w-8 items-center justify-center rounded-lg ${isEnabled ? "bg-cyan-500/20 text-cyan-400" : "bg-gray-800 text-gray-500"}`}>
            <Radio className="h-4 w-4" />
          </div>
          <div>
            <h2 className="text-sm font-semibold tracking-wide text-gray-200">
              Audio Race Engineer (Web Speech TTS)
            </h2>
            <div className="flex items-center gap-2 text-xs text-gray-400 font-mono">
              <span>Channel: Pit-to-Car</span>
              <span>•</span>
              <span className={isEnabled ? "text-emerald-400" : "text-rose-400"}>
                {isEnabled ? "Online" : "Muted"}
              </span>
            </div>
          </div>
        </div>

        {/* Master Controls */}
        <div className="flex items-center gap-3">
          <button
            onClick={handleToggleMute}
            className={`flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold shadow transition ${
              isEnabled
                ? "bg-emerald-500/20 text-emerald-300 border border-emerald-500/40 hover:bg-emerald-500/30"
                : "bg-rose-500/20 text-rose-300 border border-rose-500/40 hover:bg-rose-500/30"
            }`}
          >
            {isEnabled ? <Volume2 className="h-3.5 w-3.5" /> : <VolumeX className="h-3.5 w-3.5" />}
            {isEnabled ? "Mute Radio" : "Unmute Radio"}
          </button>
        </div>
      </div>

      {/* Speaking Indicator & Tuning Controls */}
      <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
        {/* Status card */}
        <div className="flex flex-col justify-between rounded-lg border border-gray-800 bg-[#07090e] p-3">
          <div className="flex items-center justify-between text-xs">
            <span className="font-mono text-gray-400">Radio Status:</span>
            {isSpeaking ? (
              <span className="flex items-center gap-1.5 font-semibold text-cyan-400 animate-pulse">
                <span className="h-2 w-2 rounded-full bg-cyan-400 animate-ping" />
                TRANSMITTING...
              </span>
            ) : (
              <span className="text-gray-500 font-mono">STANDBY (IDLE)</span>
            )}
          </div>

          <div className="my-2 min-h-[44px] rounded border border-gray-800/80 bg-black/40 p-2 font-mono text-xs text-gray-200 flex items-center">
            {currentMessage ? (
              <div className="flex items-start gap-2">
                {getPriorityBadge(currentMessage.priority)}
                <span className="text-gray-300 leading-snug">"{currentMessage.text}"</span>
              </div>
            ) : (
              <span className="text-gray-600 italic">No active radio transmission</span>
            )}
          </div>

          <div className="flex items-center justify-between text-[11px] font-mono text-gray-400">
            <span>Pending queue: {audioEngineer.getQueueLength()}</span>
            <button
              onClick={() => audioEngineer.cancel()}
              className="text-gray-500 hover:text-gray-300 underline"
            >
              Clear Queue
            </button>
          </div>
        </div>

        {/* Audio Tuning */}
        <div className="flex flex-col justify-between rounded-lg border border-gray-800 bg-[#07090e] p-3 text-xs">
          <div className="flex items-center justify-between">
            <label className="text-gray-400">Volume:</label>
            <div className="flex items-center gap-2">
              <input
                type="range"
                min="0"
                max="1"
                step="0.05"
                value={volume}
                onChange={handleVolumeChange}
                className="h-1.5 w-24 cursor-pointer appearance-none rounded-lg bg-gray-700 accent-cyan-400"
              />
              <span className="w-8 text-right font-mono text-gray-300">{Math.round(volume * 100)}%</span>
            </div>
          </div>

          <div className="my-2 flex items-center justify-between">
            <label className="text-gray-400">Filter Level:</label>
            <select
              value={minPriority}
              onChange={handlePriorityFilter}
              className="rounded border border-gray-700 bg-gray-900 px-2 py-1 text-xs text-gray-200 focus:outline-none focus:border-cyan-500"
            >
              <option value={AudioPriority.P4_STATUS}>All Callouts (P1 - P4)</option>
              <option value={AudioPriority.P3_TELEMETRY}>Telemetry & Critical (P1 - P3)</option>
              <option value={AudioPriority.P2_STRATEGY}>Strategy & Critical (P1 - P2)</option>
              <option value={AudioPriority.P1_CRITICAL}>P1 Critical Alerts Only</option>
            </select>
          </div>

          <div className="flex items-center justify-between">
            <label className="text-gray-400">Radio Chirp / Squelch:</label>
            <button
              onClick={handleToggleBeep}
              className={`rounded px-2 py-0.5 text-[11px] font-medium transition ${
                radioBeep ? "bg-cyan-500/20 text-cyan-300 border border-cyan-500/40" : "bg-gray-800 text-gray-500"
              }`}
            >
              {radioBeep ? "Enabled" : "Disabled"}
            </button>
          </div>
        </div>
      </div>

      {/* Manual Trigger Buttons for Demonstration & Academic Defense */}
      <div className="mt-3">
        <div className="mb-1.5 text-xs font-semibold text-gray-400">
          Manual Test Callouts (Priority Queue Simulation):
        </div>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 text-xs font-mono">
          <button
            onClick={() => triggerTestAlert("Lockup front right into turn one! Modulate braking.", AudioPriority.P1_CRITICAL)}
            className="flex items-center justify-center gap-1.5 rounded-lg border border-rose-500/40 bg-rose-500/10 px-2 py-2 text-rose-300 hover:bg-rose-500/20 transition font-medium"
          >
            <AlertOctagon className="h-3.5 w-3.5 text-rose-400" />
            <span>P1 Lockup</span>
          </button>

          <button
            onClick={() => triggerTestAlert("Box this lap for fuel and soft tyres. Confirm.", AudioPriority.P2_STRATEGY)}
            className="flex items-center justify-center gap-1.5 rounded-lg border border-amber-500/40 bg-amber-500/10 px-2 py-2 text-amber-300 hover:bg-amber-500/20 transition font-medium"
          >
            <ShieldAlert className="h-3.5 w-3.5 text-amber-400" />
            <span>P2 Box Now</span>
          </button>

          <button
            onClick={() => triggerTestAlert("Front left tyre pressure is plus zero point four PSI.", AudioPriority.P3_TELEMETRY)}
            className="flex items-center justify-center gap-1.5 rounded-lg border border-cyan-500/40 bg-cyan-500/10 px-2 py-2 text-cyan-300 hover:bg-cyan-500/20 transition font-medium"
          >
            <Zap className="h-3.5 w-3.5 text-cyan-400" />
            <span>P3 Tyre Alert</span>
          </button>

          <button
            onClick={() => triggerTestAlert("Sector one purple. Delta minus zero point two five.", AudioPriority.P4_STATUS)}
            className="flex items-center justify-center gap-1.5 rounded-lg border border-emerald-500/40 bg-emerald-500/10 px-2 py-2 text-emerald-300 hover:bg-emerald-500/20 transition font-medium"
          >
            <CheckCircle className="h-3.5 w-3.5 text-emerald-400" />
            <span>P4 Sector Split</span>
          </button>
        </div>
      </div>

      {/* Recent Callout History */}
      <div className="mt-3">
        <div className="mb-1 text-xs font-semibold text-gray-400">Transmission History:</div>
        <div className="max-h-28 overflow-y-auto space-y-1 rounded-lg border border-gray-800/80 bg-[#07090e] p-2 text-xs font-mono">
          {recentCallouts.length === 0 ? (
            <div className="text-gray-600 text-center py-2">No callouts logged yet</div>
          ) : (
            recentCallouts.slice(0, 8).map((msg) => (
              <div key={msg.id} className="flex items-center justify-between border-b border-gray-900 pb-1 last:border-0 last:pb-0">
                <div className="flex items-center gap-2 overflow-hidden text-ellipsis whitespace-nowrap">
                  {getPriorityBadge(msg.priority)}
                  <span className="text-gray-300">{msg.text}</span>
                </div>
                <span className="text-[10px] text-gray-500 ml-2 shrink-0">
                  {new Date(msg.timestamp).toLocaleTimeString()}
                </span>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};
