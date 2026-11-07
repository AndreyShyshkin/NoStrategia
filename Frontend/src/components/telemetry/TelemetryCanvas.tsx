"use client";

import React, { useEffect, useRef, useState } from "react";
import { TelemetryBuffers } from "../../hooks/useTelemetryWebSocket";
import { formatGear, SessionPhase } from "../../types/telemetry";
import { Play, Pause, Eye, RotateCcw } from "lucide-react";

interface TelemetryCanvasProps {
  buffers: TelemetryBuffers;
  height?: number;
  showFps?: boolean;
  className?: string;
  isLive?: boolean;
  streamSource?: "agent" | "waiting_for_agent" | "simulator";
  sessionPhase?: SessionPhase;
  phaseDetails?: string;
}

interface HoverInspectionData {
  x: number;
  speed: number;
  gas: number;
  brake: number;
  steer: number;
  rpm: number;
  gear: number;
  distNorm: number;
  timeDeltaSec: number;
}

export const TelemetryCanvas: React.FC<TelemetryCanvasProps> = ({
  buffers,
  height = 420,
  showFps = true,
  className = "",
  isLive = false,
  streamSource = "waiting_for_agent",
  sessionPhase = "WAITING_FOR_AGENT",
  phaseDetails = "Очікування запуску Telemetry Agent...",
}) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const [isPaused, setIsPaused] = useState<boolean>(false);
  const isPausedRef = useRef<boolean>(false);
  isPausedRef.current = isPaused;

  const [visibleChannels, setVisibleChannels] = useState({
    speed: true,
    throttle: true,
    brake: true,
    steer: true,
    rpm: true,
  });

  const [fps, setFps] = useState<number>(60);
  const [frameTimeMs, setFrameTimeMs] = useState<number>(16.6);
  const [hoverData, setHoverData] = useState<HoverInspectionData | null>(null);

  // High-frequency render loop state in refs (Zero-GC)
  const fpsFrameCount = useRef(0);
  const fpsLastTime = useRef(performance.now());
  const lastRenderTime = useRef(performance.now());
  const pausedSnapshot = useRef<{
    speed: Float32Array;
    gas: Float32Array;
    brake: Float32Array;
    steer: Float32Array;
    rpm: Float32Array;
    distNorm: Float32Array;
    head: number;
  } | null>(null);

  // Toggle channel visibility
  const toggleChannel = (channel: keyof typeof visibleChannels) => {
    setVisibleChannels((prev) => ({ ...prev, [channel]: !prev[channel] }));
  };

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    let animationFrameId: number;

    const render = (now: number) => {
      // Calculate instantaneous and rolling FPS
      const delta = now - lastRenderTime.current;
      lastRenderTime.current = now;

      fpsFrameCount.current++;
      if (now - fpsLastTime.current >= 500) {
        const calculatedFps = (fpsFrameCount.current * 1000) / (now - fpsLastTime.current);
        setFps(Math.round(calculatedFps));
        setFrameTimeMs(parseFloat(delta.toFixed(1)));
        fpsFrameCount.current = 0;
        fpsLastTime.current = now;
      }

      const rect = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      const logicalWidth = rect.width || 900;
      const logicalHeight = height;

      // Ensure bitmap size exactly matches physical pixels (Retina sharp)
      const targetBitmapWidth = Math.floor(logicalWidth * dpr);
      const targetBitmapHeight = Math.floor(logicalHeight * dpr);

      if (canvas.width !== targetBitmapWidth || canvas.height !== targetBitmapHeight) {
        canvas.width = targetBitmapWidth;
        canvas.height = targetBitmapHeight;
        canvas.style.width = `${logicalWidth}px`;
        canvas.style.height = `${logicalHeight}px`;
      }

      const ctx = canvas.getContext("2d", { alpha: false });
      if (ctx) {
        // Reset transform to identity and scale by DPR for subpixel rendering
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        // Render using LOGICAL coordinates so lower zone is never pushed off-screen
        drawCanvas(
          ctx,
          logicalWidth,
          logicalHeight,
          buffers,
          isPausedRef.current ? pausedSnapshot.current : null,
          visibleChannels
        );
      }

      animationFrameId = requestAnimationFrame(render);
    };

    animationFrameId = requestAnimationFrame(render);

    return () => {
      cancelAnimationFrame(animationFrameId);
    };
  }, [buffers, visibleChannels, height]);

  // Pause / Resume handler
  const handleTogglePause = () => {
    if (!isPaused) {
      const cap = buffers.capacity;
      pausedSnapshot.current = {
        speed: new Float32Array(buffers.speed),
        gas: new Float32Array(buffers.gas),
        brake: new Float32Array(buffers.brake),
        steer: new Float32Array(buffers.steer),
        rpm: new Float32Array(buffers.rpm),
        distNorm: new Float32Array(buffers.distNorm),
        head: buffers.headIndex.current,
      };
      setIsPaused(true);
    } else {
      pausedSnapshot.current = null;
      setIsPaused(false);
      setHoverData(null);
    }
  };

  // Interactive Mouse Hover for crosshair and live values
  const handleMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left;
    const width = rect.width;

    const cap = buffers.capacity;
    const head = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.head : buffers.headIndex.current;
    const startIdx = head >= cap ? head % cap : 0;

    const ratio = Math.max(0, Math.min(1, mouseX / width));
    const pointOffset = Math.floor(ratio * cap);
    const bufferPos = (startIdx + pointOffset) % cap;

    const sourceSpeed = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.speed : buffers.speed;
    const sourceGas = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.gas : buffers.gas;
    const sourceBrake = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.brake : buffers.brake;
    const sourceSteer = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.steer : buffers.steer;
    const sourceRpm = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.rpm : buffers.rpm;
    const sourceDist = isPausedRef.current && pausedSnapshot.current ? pausedSnapshot.current.distNorm : buffers.distNorm;

    const rawSteer = sourceSteer[bufferPos];
    const steerDeg = Math.abs(rawSteer) <= 3.5 ? rawSteer * (180.0 / Math.PI) : rawSteer;

    const timeDeltaSec = ((pointOffset - cap) / 60);
    const latest = buffers.latestPacket.current;
    const currentRawGear = latest ? latest.gear : 1;

    setHoverData({
      x: mouseX,
      speed: Math.round(sourceSpeed[bufferPos]),
      gas: Math.round(sourceGas[bufferPos] * 100),
      brake: Math.round(sourceBrake[bufferPos] * 100),
      steer: parseFloat(steerDeg.toFixed(1)),
      rpm: Math.round(sourceRpm[bufferPos]),
      gear: currentRawGear,
      distNorm: parseFloat(sourceDist[bufferPos].toFixed(3)),
      timeDeltaSec: parseFloat(timeDeltaSec.toFixed(2)),
    });
  };

  const handleMouseLeave = () => {
    setHoverData(null);
  };

  return (
    <div
      ref={containerRef}
      className={`relative flex flex-col rounded-xl border border-gray-800 bg-[#0d1117] p-4 shadow-2xl ${className}`}
    >
      {/* Canvas Header / Controls */}
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3 border-b border-gray-800/80 pb-3">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <span className="relative flex h-3 w-3">
              <span
                className={`absolute inline-flex h-full w-full animate-ping rounded-full ${
                  isLive ? "bg-emerald-400" : isPaused ? "bg-amber-400" : "bg-yellow-400"
                } opacity-75`}
              ></span>
              <span
                className={`relative inline-flex h-3 w-3 rounded-full ${
                  isLive ? "bg-emerald-500" : isPaused ? "bg-amber-500" : "bg-yellow-500"
                }`}
              ></span>
            </span>
            <h2 className="text-sm font-semibold tracking-wider text-gray-200 uppercase">
              {isLive
                ? "Live Telemetry Stream (60 Hz Canvas)"
                : streamSource === "simulator"
                ? "Demo Simulation (60 Hz Canvas)"
                : "Awaiting Telemetry Agent (In Lobby / Standby)"}
            </h2>
          </div>

          {showFps && isLive && (
            <div className="flex items-center gap-2 rounded bg-black/60 px-2 py-0.5 text-xs font-mono text-gray-300 border border-gray-800">
              <span className="text-gray-400">FPS:</span>
              <span className={`font-bold ${fps >= 55 ? "text-emerald-400" : fps >= 30 ? "text-amber-400" : "text-rose-400"}`}>
                {fps}
              </span>
              <span className="text-gray-500">({frameTimeMs}ms)</span>
            </div>
          )}
        </div>

        {/* Channel Toggles & Pause Control */}
        <div className="flex items-center gap-2 text-xs">
          <button
            onClick={() => toggleChannel("throttle")}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-medium transition ${
              visibleChannels.throttle
                ? "bg-emerald-500/20 text-emerald-400 border border-emerald-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            <span className="h-2 w-2 rounded-full bg-emerald-400" />
            <span>Throttle</span>
          </button>
          <button
            onClick={() => toggleChannel("brake")}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-medium transition ${
              visibleChannels.brake
                ? "bg-rose-500/20 text-rose-400 border border-rose-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            <span className="h-2 w-2 rounded-full bg-rose-400" />
            <span>Brake</span>
          </button>
          <button
            onClick={() => toggleChannel("speed")}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-medium transition ${
              visibleChannels.speed
                ? "bg-cyan-500/20 text-cyan-400 border border-cyan-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            <span className="h-2 w-2 rounded-full bg-cyan-400" />
            <span>Speed</span>
          </button>
          <button
            onClick={() => toggleChannel("steer")}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-medium transition ${
              visibleChannels.steer
                ? "bg-amber-500/20 text-amber-400 border border-amber-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            <span className="h-2 w-2 rounded-full bg-amber-400" />
            <span>Steer</span>
          </button>
          <button
            onClick={() => toggleChannel("rpm")}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 font-medium transition ${
              visibleChannels.rpm
                ? "bg-purple-500/20 text-purple-400 border border-purple-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            <span className="h-2 w-2 rounded-full bg-purple-400" />
            <span>RPM</span>
          </button>

          <div className="h-4 w-px bg-gray-700 mx-1" />

          <button
            onClick={handleTogglePause}
            className={`flex items-center gap-1.5 rounded px-2.5 py-1 text-xs font-semibold shadow transition ${
              isPaused
                ? "bg-amber-500 text-black hover:bg-amber-400"
                : "bg-gray-800 text-gray-200 hover:bg-gray-700 border border-gray-700"
            }`}
          >
            {isPaused ? <Play className="h-3 w-3 fill-current" /> : <Pause className="h-3 w-3 fill-current" />}
            {isPaused ? "Resume" : "Pause"}
          </button>
        </div>
      </div>

      {/* Main Canvas Area */}
      <div className="relative w-full cursor-crosshair overflow-hidden rounded-lg bg-[#07090e] border border-gray-900">
        <canvas
          ref={canvasRef}
          onMouseMove={handleMouseMove}
          onMouseLeave={handleMouseLeave}
          className="block w-full"
        />

        {/* Standby / Waiting for Agent Overlay */}
        {!isLive && !isPaused && (
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center bg-black/40 backdrop-blur-[2px]">
            <div
              className={`flex items-center gap-2 rounded-full border px-4 py-1.5 text-xs font-mono shadow-xl ${
                sessionPhase === "IN_LOBBY"
                  ? "border-amber-500/40 bg-amber-950/70 text-amber-300"
                  : sessionPhase === "IN_GARAGE"
                  ? "border-cyan-500/40 bg-cyan-950/70 text-cyan-300"
                  : sessionPhase === "IN_PIT_LANE"
                  ? "border-orange-500/40 bg-orange-950/70 text-orange-300"
                  : sessionPhase === "SESSION_PAUSED"
                  ? "border-blue-500/40 bg-blue-950/70 text-blue-300"
                  : "border-rose-500/40 bg-rose-950/70 text-rose-300"
              }`}
            >
              <span className="relative flex h-2.5 w-2.5">
                <span
                  className={`absolute inline-flex h-full w-full animate-ping rounded-full opacity-75 ${
                    sessionPhase === "IN_LOBBY"
                      ? "bg-amber-400"
                      : sessionPhase === "IN_GARAGE"
                      ? "bg-cyan-400"
                      : sessionPhase === "IN_PIT_LANE"
                      ? "bg-orange-400"
                      : sessionPhase === "SESSION_PAUSED"
                      ? "bg-blue-400"
                      : "bg-rose-400"
                  }`}
                ></span>
                <span
                  className={`relative inline-flex h-2.5 w-2.5 rounded-full ${
                    sessionPhase === "IN_LOBBY"
                      ? "bg-amber-500"
                      : sessionPhase === "IN_GARAGE"
                      ? "bg-cyan-500"
                      : sessionPhase === "IN_PIT_LANE"
                      ? "bg-orange-500"
                      : sessionPhase === "SESSION_PAUSED"
                      ? "bg-blue-500"
                      : "bg-rose-500"
                  }`}
                ></span>
              </span>
              <span className="font-semibold tracking-wider uppercase">
                {sessionPhase === "IN_LOBBY"
                  ? "Агент підключений • В лобі / Меню"
                  : sessionPhase === "IN_GARAGE"
                  ? "Бокси / Гараж • Налаштування сетапу"
                  : sessionPhase === "IN_PIT_LANE"
                  ? "Піт-лейн • Обмежувач активний (10 Гц)"
                  : sessionPhase === "SESSION_PAUSED"
                  ? "Сесію призупинено в меню паузи"
                  : "Очікування запуску Telemetry Agent"}
              </span>
            </div>
            <p className="mt-2 text-[11px] font-mono text-gray-400">
              {phaseDetails}
            </p>
          </div>
        )}

        {/* Hover Crosshair Overlay */}
        {hoverData && (
          <>
            <div
              className="pointer-events-none absolute bottom-0 top-0 w-px bg-white/70 shadow-[0_0_8px_white]"
              style={{ left: `${hoverData.x}px` }}
            />
            <div
              className="pointer-events-none absolute top-3 z-20 flex items-center gap-3 rounded-lg border border-gray-700 bg-gray-950/90 px-3 py-1.5 text-xs font-mono shadow-xl backdrop-blur-md"
              style={{
                left: `${Math.min(
                  Math.max(10, hoverData.x - 120),
                  (containerRef.current?.clientWidth || 900) - 280
                )}px`,
              }}
            >
              <div className="text-gray-400">{hoverData.timeDeltaSec}s</div>
              {visibleChannels.speed && (
                <div className="text-cyan-400 font-bold">{hoverData.speed} km/h</div>
              )}
              <div className="text-purple-400 font-bold">G: {formatGear(hoverData.gear)}</div>
              {visibleChannels.throttle && (
                <div className="text-emerald-400 font-bold">T: {hoverData.gas}%</div>
              )}
              {visibleChannels.brake && (
                <div className="text-rose-400 font-bold">B: {hoverData.brake}%</div>
              )}
              {visibleChannels.steer && (
                <div className="text-amber-400 font-bold">S: {hoverData.steer}°</div>
              )}
              {visibleChannels.rpm && (
                <div className="text-purple-400 font-bold">{hoverData.rpm} RPM</div>
              )}
            </div>
          </>
        )}
      </div>

      {/* Canvas Timeline Legend / Time Reference */}
      <div className="mt-2 flex items-center justify-between text-[11px] font-mono text-gray-500">
        <span>-20.0s (Past)</span>
        <span>-15.0s</span>
        <span>-10.0s</span>
        <span>-5.0s</span>
        <span className="text-cyan-400 font-bold">Now (Live Edge)</span>
      </div>
    </div>
  );
};

/**
 * Fast Uncontrolled Canvas Rendering Engine (Zero Garbage Collection)
 * Renders in exact logical coordinates (width, height) scaled by DPR
 */
function drawCanvas(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  buffers: TelemetryBuffers,
  snapshot: {
    speed: Float32Array;
    gas: Float32Array;
    brake: Float32Array;
    steer: Float32Array;
    rpm: Float32Array;
    head: number;
  } | null,
  visible: {
    speed: boolean;
    throttle: boolean;
    brake: boolean;
    steer: boolean;
    rpm: boolean;
  }
) {
  const cap = buffers.capacity;
  const head = snapshot ? snapshot.head : buffers.headIndex.current;
  const startIdx = head >= cap ? head % cap : 0;

  const speedData = snapshot ? snapshot.speed : buffers.speed;
  const gasData = snapshot ? snapshot.gas : buffers.gas;
  const brakeData = snapshot ? snapshot.brake : buffers.brake;
  const steerData = snapshot ? snapshot.steer : buffers.steer;
  const rpmData = snapshot ? snapshot.rpm : buffers.rpm;

  // Clear canvas background
  ctx.fillStyle = "#07090e";
  ctx.fillRect(0, 0, width, height);

  // Divide canvas cleanly into 2 visual telemetry zones:
  // Zone 1: Speed & RPM (0% to 48% height)
  // Zone 2: Pedals & Steering (50% to 100% height)
  const zoneSplitY = Math.floor(height * 0.48);

  // Grid background lines
  ctx.lineWidth = 1;
  ctx.strokeStyle = "#131924";

  // Horizontal guidelines across zones
  const hLines = [0.12, 0.24, 0.36, 0.62, 0.76, 0.88];
  for (const ratio of hLines) {
    const y = Math.floor(height * ratio);
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }

  // Zone separator line
  ctx.strokeStyle = "#253147";
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(0, zoneSplitY);
  ctx.lineTo(width, zoneSplitY);
  ctx.stroke();

  // Vertical time division lines (every 2 seconds)
  const vDivisions = 10;
  ctx.strokeStyle = "#121721";
  ctx.lineWidth = 1;
  for (let i = 1; i < vDivisions; i++) {
    const x = Math.floor((width / vDivisions) * i);
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
  }

  // -------------------------------------------------------------
  // ZONE 1: Speed & Engine RPM (Upper Zone: 16px to zoneSplitY - 14px)
  // -------------------------------------------------------------
  const upperTopY = 16;
  const upperBottomY = zoneSplitY - 14;
  const upperUsableHeight = upperBottomY - upperTopY;

  // Axis Labels & Legends for Zone 1
  ctx.font = "10px JetBrains Mono, monospace";
  ctx.fillStyle = "#4b5563";
  ctx.fillText("300 km/h", 8, upperTopY + 4);
  ctx.fillText("150 km/h", 8, upperTopY + upperUsableHeight * 0.5 + 4);
  ctx.fillText("0 km/h", 8, upperBottomY + 2);

  if (visible.rpm) {
    ctx.textAlign = "right";
    ctx.fillText("9000 RPM", width - 8, upperTopY + 4);
    ctx.fillText("4500 RPM", width - 8, upperTopY + upperUsableHeight * 0.5 + 4);
    ctx.textAlign = "left";
  }

  const stepX = width / (cap - 1);

  // 1. Draw RPM (Purple dashed) in upper zone
  if (visible.rpm) {
    ctx.strokeStyle = "#c084fc";
    ctx.lineWidth = 1.4;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = rpmData[idx]; // 0..9000
      const norm = Math.min(1.0, Math.max(0.0, val / 8500));
      const x = i * stepX;
      const y = upperBottomY - norm * upperUsableHeight;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.setLineDash([]);
  }

  // 2. Draw Speed (Cyan) in upper zone with subtle glowing fill
  if (visible.speed) {
    // Subtle area fill underneath speed trace
    ctx.fillStyle = "rgba(6, 182, 212, 0.08)";
    ctx.beginPath();
    ctx.moveTo(0, upperBottomY);
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = speedData[idx]; // 0..300 km/h
      const norm = Math.min(1.0, Math.max(0.0, val / 300));
      const x = i * stepX;
      const y = upperBottomY - norm * upperUsableHeight;
      ctx.lineTo(x, y);
    }
    ctx.lineTo(width, upperBottomY);
    ctx.closePath();
    ctx.fill();

    // Speed stroke line
    ctx.strokeStyle = "#06b6d4";
    ctx.lineWidth = 2.2;
    ctx.shadowColor = "#06b6d4";
    ctx.shadowBlur = 4;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = speedData[idx];
      const norm = Math.min(1.0, Math.max(0.0, val / 300));
      const x = i * stepX;
      const y = upperBottomY - norm * upperUsableHeight;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // -------------------------------------------------------------
  // ZONE 2: Pedals & Steering (Lower Zone: zoneSplitY + 16px to height - 16px)
  // -------------------------------------------------------------
  const lowerTopY = zoneSplitY + 16;
  const lowerBottomY = height - 18;
  const lowerUsableHeight = lowerBottomY - lowerTopY;
  const steerCenterY = Math.floor(lowerTopY + lowerUsableHeight * 0.5);

  // Baseline and Guides for Zone 2
  ctx.strokeStyle = "#1b2434";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(0, lowerBottomY);
  ctx.lineTo(width, lowerBottomY);
  ctx.stroke();

  // Steer center 0° line (subtle amber dashed)
  ctx.strokeStyle = "#2e2712";
  ctx.setLineDash([3, 4]);
  ctx.beginPath();
  ctx.moveTo(0, steerCenterY);
  ctx.lineTo(width, steerCenterY);
  ctx.stroke();
  ctx.setLineDash([]);

  // Axis Labels & Legends for Zone 2
  ctx.font = "10px JetBrains Mono, monospace";
  ctx.fillStyle = "#4b5563";
  ctx.fillText("100% Pedals", 8, lowerTopY + 2);
  ctx.fillText("50%", 8, steerCenterY + 3);
  ctx.fillText("0% Pedals", 8, lowerBottomY + 12);

  if (visible.steer) {
    ctx.textAlign = "right";
    ctx.fillStyle = "#854d0e";
    ctx.fillText("+60° Steer", width - 8, lowerTopY + 2);
    ctx.fillText("0° (Center)", width - 8, steerCenterY + 3);
    ctx.fillText("-60° Steer", width - 8, lowerBottomY + 12);
    ctx.textAlign = "left";
  }

  // 3. Draw Steering Angle (Yellow/Amber) in lower zone
  if (visible.steer) {
    ctx.strokeStyle = "#facc15";
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const raw = steerData[idx];
      // Normalize angle: if in radians (|val| <= 3.5), convert to degrees then scale by 60°
      const deg = Math.abs(raw) <= 3.5 ? raw * (180.0 / Math.PI) : raw;
      const normSteer = Math.max(-1.0, Math.min(1.0, deg / 60.0));
      const x = i * stepX;
      const y = steerCenterY - normSteer * (lowerUsableHeight * 0.45);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
  }

  // 4. Draw Gas / Throttle (Neon Green) in lower zone with area fill
  if (visible.throttle) {
    // Area fill under throttle
    ctx.fillStyle = "rgba(34, 197, 94, 0.15)";
    ctx.beginPath();
    ctx.moveTo(0, lowerBottomY);
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = Math.max(0.0, Math.min(1.0, gasData[idx]));
      const x = i * stepX;
      const y = lowerBottomY - val * lowerUsableHeight;
      ctx.lineTo(x, y);
    }
    ctx.lineTo(width, lowerBottomY);
    ctx.closePath();
    ctx.fill();

    // Throttle stroke line
    ctx.strokeStyle = "#22c55e";
    ctx.lineWidth = 2.2;
    ctx.shadowColor = "#22c55e";
    ctx.shadowBlur = 3;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = Math.max(0.0, Math.min(1.0, gasData[idx]));
      const x = i * stepX;
      const y = lowerBottomY - val * lowerUsableHeight;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // 5. Draw Brake (Racing Red) in lower zone with area fill
  if (visible.brake) {
    // Area fill under brake
    ctx.fillStyle = "rgba(239, 68, 68, 0.20)";
    ctx.beginPath();
    ctx.moveTo(0, lowerBottomY);
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = Math.max(0.0, Math.min(1.0, brakeData[idx]));
      const x = i * stepX;
      const y = lowerBottomY - val * lowerUsableHeight;
      ctx.lineTo(x, y);
    }
    ctx.lineTo(width, lowerBottomY);
    ctx.closePath();
    ctx.fill();

    // Brake stroke line
    ctx.strokeStyle = "#ef4444";
    ctx.lineWidth = 2.2;
    ctx.shadowColor = "#ef4444";
    ctx.shadowBlur = 3;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = Math.max(0.0, Math.min(1.0, brakeData[idx]));
      const x = i * stepX;
      const y = lowerBottomY - val * lowerUsableHeight;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // Draw current live needle/edge indicator at the rightmost edge
  ctx.strokeStyle = "#38bdf8";
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(width - 1, 0);
  ctx.lineTo(width - 1, height);
  ctx.stroke();
}
