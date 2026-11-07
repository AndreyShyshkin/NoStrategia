"use client";

import React, { useState } from "react";
import { useTelemetryWebSocket } from "../hooks/useTelemetryWebSocket";
import { TelemetryCanvas } from "../components/telemetry/TelemetryCanvas";
import { TelemetryGauges } from "../components/telemetry/TelemetryGauges";
import { AudioEngineerWidget } from "../components/audio/AudioEngineerWidget";
import { AudioRaceEngineer, AudioMessage } from "../services/AudioRaceEngineer";
import { TacticalEvent, AudioPriority } from "../types/telemetry";
import { Wifi, WifiOff, Car, MapPin, Radio, Terminal, Settings2, RefreshCw, Play, Square, Layers } from "lucide-react";

export default function TelemetryDashboard() {
  const [audioEngineerInstance] = useState(() => new AudioRaceEngineer());
  const [recentCallouts, setRecentCallouts] = useState<AudioMessage[]>([]);
  const [customWsUrl, setCustomWsUrl] = useState<string>("ws://localhost:4001/ws/stream");
  const [showSettings, setShowSettings] = useState<boolean>(false);

  // Handle incoming tactical / strategy events from the WebSocket stream
  const handleTacticalEvent = (event: TacticalEvent) => {
    const audioMsg: AudioMessage = {
      id: event.id,
      priority: event.priority,
      text: event.text,
      category: event.category,
      timestamp: event.timestamp,
    };

    setRecentCallouts((prev) => [audioMsg, ...prev.slice(0, 19)]);
    audioEngineerInstance.postCallout(event.text, event.priority, event.category);
  };

  const {
    isConnected,
    isConnecting,
    serverStatus,
    streamSource,
    isLive,
    agentConnected,
    sessionPhase,
    phaseDetails,
    showCarAndTrack,
    carModel,
    track,
    simulationEnabled,
    lastPing,
    buffers,
    connect,
    disconnect,
    sendEvent,
    toggleSimulation,
  } = useTelemetryWebSocket({
    url: customWsUrl,
    bufferCapacity: 1200,
    autoConnect: true,
    onEvent: handleTacticalEvent,
  });

  const handleManualTrigger = (text: string, priority: AudioPriority) => {
    sendEvent(text, priority);
    audioEngineerInstance.postCallout(text, priority);

    const manualMsg: AudioMessage = {
      id: `manual-${Date.now()}`,
      priority,
      text,
      category: "MANUAL",
      timestamp: Date.now(),
    };
    setRecentCallouts((prev) => [manualMsg, ...prev.slice(0, 19)]);
  };

  const getSessionPhaseBadge = () => {
    if (!isConnected) {
      return {
        text: "Шлюз відключено",
        color: "bg-rose-500",
        textColor: "text-rose-400",
        pingColor: "bg-rose-400",
      };
    }
    if (simulationEnabled) {
      return {
        text: "Демо-симулятор (Spa 60 FPS)",
        color: "bg-purple-500",
        textColor: "text-purple-400",
        pingColor: "bg-purple-400",
      };
    }
    switch (sessionPhase) {
      case "LIVE_ON_TRACK":
        return {
          text: "На трасі (Live 60 Гц)",
          color: "bg-emerald-500",
          textColor: "text-emerald-400",
          pingColor: "bg-emerald-400",
        };
      case "IN_PIT_LANE":
        return {
          text: "Піт-лейн (10 Гц)",
          color: "bg-orange-500",
          textColor: "text-orange-400",
          pingColor: "bg-orange-400",
        };
      case "IN_GARAGE":
        return {
          text: "Гараж / Бокси",
          color: "bg-cyan-500",
          textColor: "text-cyan-400",
          pingColor: "bg-cyan-400",
        };
      case "SESSION_PAUSED":
        return {
          text: "Пауза сесії",
          color: "bg-blue-500",
          textColor: "text-blue-400",
          pingColor: "bg-blue-400",
        };
      case "REPLAY":
        return {
          text: "Реплей",
          color: "bg-purple-500",
          textColor: "text-purple-400",
          pingColor: "bg-purple-400",
        };
      case "IN_LOBBY":
        return {
          text: "Агент підключений • В лобі",
          color: "bg-amber-500",
          textColor: "text-amber-400",
          pingColor: "bg-amber-400",
        };
      case "WAITING_FOR_AGENT":
      default:
        return {
          text: "Очікування агента",
          color: "bg-rose-500",
          textColor: "text-rose-400",
          pingColor: "bg-rose-400",
        };
    }
  };

  const badgeInfo = getSessionPhaseBadge();

  return (
    <div className="flex min-h-screen flex-col bg-[#07090e] text-gray-100">
      {/* 1. Header / Top Navigation Bar */}
      <header className="sticky top-0 z-50 border-b border-gray-800/80 bg-[#0d1117]/90 backdrop-blur-md px-4 py-2.5">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-3">
          {/* Logo & Platform Info */}
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-cyan-500 to-blue-600 font-bold text-black shadow-lg shadow-cyan-500/20">
              NS
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h1 className="text-base font-bold tracking-tight text-white">NoStrategia</h1>
                <span className="rounded bg-cyan-950 px-1.5 py-0.5 text-[10px] font-semibold text-cyan-400 border border-cyan-800/60">
                  Telemetry 60 FPS
                </span>
              </div>
              <p className="text-xs text-gray-400">Assetto Corsa Competizione High-Performance Telemetry Platform</p>
            </div>
          </div>

          {/* Car & Track Badges (STRICTLY shown only when on track / session; HIDDEN in lobby) */}
          <div className="hidden sm:flex items-center gap-2 text-xs font-mono">
            {showCarAndTrack ? (
              <>
                <div className="flex items-center gap-1.5 rounded-lg border border-gray-800 bg-black/60 px-2.5 py-1 text-gray-200 font-semibold shadow-inner">
                  <Car className="h-3.5 w-3.5 text-cyan-400" />
                  <span>{carModel}</span>
                </div>
                <div className="flex items-center gap-1.5 rounded-lg border border-gray-800 bg-black/60 px-2.5 py-1 text-gray-200 font-semibold shadow-inner">
                  <MapPin className="h-3.5 w-3.5 text-emerald-400" />
                  <span>{track}</span>
                </div>
              </>
            ) : (
              <div className="flex items-center gap-1.5 rounded-lg border border-gray-800/80 bg-black/30 px-3 py-1 text-gray-500 italic">
                <Layers className="h-3.5 w-3.5 text-gray-600" />
                <span>
                  {agentConnected
                    ? "В лобі гри (вибір автомобіля та траси)"
                    : "Очікування запуску TelemetryAgent"}
                </span>
              </div>
            )}
          </div>

          {/* WebSocket Connection Indicator & Actions */}
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-2 rounded-lg border border-gray-800 bg-black/60 px-2.5 py-1 text-xs font-mono">
              <span className="relative flex h-2 w-2">
                {isConnected && (
                  <span
                    className={`absolute inline-flex h-full w-full animate-ping rounded-full ${badgeInfo.pingColor} opacity-75`}
                  ></span>
                )}
                <span className={`relative inline-flex h-2 w-2 rounded-full ${badgeInfo.color}`} />
              </span>
              <span className={`${badgeInfo.textColor} font-semibold`}>
                {badgeInfo.text}
              </span>
              {isConnected && lastPing > 0 && (
                <span className="text-gray-500 text-[11px]">({lastPing}ms)</span>
              )}
            </div>

            <button
              onClick={() => (isConnected ? disconnect() : connect())}
              className={`flex items-center gap-1 rounded-lg px-2.5 py-1 text-xs font-medium transition ${
                isConnected
                  ? "bg-rose-500/10 text-rose-300 border border-rose-500/30 hover:bg-rose-500/20"
                  : "bg-emerald-500/10 text-emerald-300 border border-emerald-500/30 hover:bg-emerald-500/20"
              }`}
            >
              {isConnected ? <WifiOff className="h-3 w-3" /> : <Wifi className="h-3 w-3" />}
              <span>{isConnected ? "Disconnect" : "Connect"}</span>
            </button>

            <button
              onClick={() => setShowSettings(!showSettings)}
              className="rounded-lg border border-gray-800 bg-gray-900 p-1.5 text-gray-400 hover:text-white transition"
              title="Connection Settings"
            >
              <Settings2 className="h-4 w-4" />
            </button>
          </div>
        </div>

        {/* Optional Connection & Simulation Settings Drawer */}
        {showSettings && (
          <div className="mx-auto mt-2.5 max-w-7xl rounded-lg border border-gray-800 bg-black/90 p-3 text-xs font-mono shadow-2xl">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="flex flex-wrap items-center gap-3">
                <span className="text-gray-400">WebSocket Endpoint:</span>
                <input
                  type="text"
                  value={customWsUrl}
                  onChange={(e) => setCustomWsUrl(e.target.value)}
                  className="w-72 rounded border border-gray-700 bg-gray-900 px-2 py-1 text-cyan-300 focus:outline-none focus:border-cyan-500"
                />
                <button
                  onClick={() => {
                    disconnect();
                    setTimeout(() => connect(), 200);
                  }}
                  className="flex items-center gap-1 rounded bg-cyan-600 px-2.5 py-1 text-white hover:bg-cyan-500 transition"
                >
                  <RefreshCw className="h-3 w-3" />
                  Reconnect
                </button>
              </div>

              {/* Simulation Mode Toggle (strictly OFF by default: waits for real TelemetryAgent) */}
              <div className="flex items-center gap-3 border-t sm:border-t-0 sm:border-l border-gray-800 pt-2 sm:pt-0 sm:pl-4">
                <span className="text-gray-400">Offline Simulation Mode:</span>
                <button
                  onClick={() => toggleSimulation(!simulationEnabled)}
                  className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-semibold transition ${
                    simulationEnabled
                      ? "bg-purple-600 text-white hover:bg-purple-500"
                      : "bg-gray-800 text-gray-400 hover:bg-gray-700 border border-gray-700"
                  }`}
                >
                  {simulationEnabled ? <Square className="h-3 w-3 fill-current" /> : <Play className="h-3 w-3 fill-current" />}
                  {simulationEnabled ? "Stop Demo (Wait for Agent)" : "Start Demo Simulator"}
                </button>
              </div>
            </div>
          </div>
        )}
      </header>

      {/* 2. Main Dashboard Body */}
      <main className="mx-auto w-full max-w-7xl flex-1 space-y-4 p-4">
        {/* HUD Instantaneous Gauges */}
        <section>
          <TelemetryGauges
            buffers={buffers}
            isLive={isLive}
            sessionPhase={sessionPhase}
          />
        </section>

        {/* 60 FPS Uncontrolled Canvas Live Charts */}
        <section>
          <TelemetryCanvas
            buffers={buffers}
            height={380}
            showFps={true}
            isLive={isLive}
            streamSource={streamSource}
            sessionPhase={sessionPhase}
            phaseDetails={phaseDetails}
          />
        </section>

        {/* Audio Race Engineer Subsystem & Controls */}
        <section>
          <AudioEngineerWidget
            audioEngineer={audioEngineerInstance}
            recentCallouts={recentCallouts}
            onTriggerCallout={handleManualTrigger}
          />
        </section>
      </main>

      {/* 3. Footer */}
      <footer className="border-t border-gray-900 bg-[#0a0c10] px-4 py-3 text-center text-xs font-mono text-gray-600">
        NoStrategia • Edge Agent Ingestion & Redis Stream Pipeline • 60 FPS Canvas & Web Speech Engine
      </footer>
    </div>
  );
}
