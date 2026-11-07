"use client";

import React, { useEffect, useRef, useState } from "react";
import { TelemetryBuffers } from "../../hooks/useTelemetryWebSocket";
import { Play, Pause, Eye, RotateCcw } from "lucide-react";

interface TelemetryCanvasProps {
  buffers: TelemetryBuffers;
  height?: number;
  showFps?: boolean;
  className?: string;
}

interface HoverInspectionData {
  x: number;
  speed: number;
  gas: number;
  brake: number;
  steer: number;
  rpm: number;
  distNorm: number;
  timeDeltaSec: number;
}

export const TelemetryCanvas: React.FC<TelemetryCanvasProps> = ({
  buffers,
  height = 420,
  showFps = true,
  className = "",
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
    rpm: false,
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
      // Calculate instantaneous and average FPS
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

      const ctx = canvas.getContext("2d", { alpha: false });
      if (ctx) {
        // Draw the canvas using either live circular buffers or paused snapshot
        drawCanvas(
          ctx,
          canvas.width,
          canvas.height,
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
  }, [buffers, visibleChannels]);

  // Handle Canvas Resizing with DevicePixelRatio
  useEffect(() => {
    const handleResize = () => {
      const container = containerRef.current;
      const canvas = canvasRef.current;
      if (!container || !canvas) return;

      const rect = container.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;

      canvas.width = Math.floor(rect.width * dpr);
      canvas.height = Math.floor(height * dpr);
      canvas.style.width = `${rect.width}px`;
      canvas.style.height = `${height}px`;

      const ctx = canvas.getContext("2d");
      if (ctx) {
        ctx.scale(dpr, dpr);
      }
    };

    handleResize();
    const resizeObserver = new ResizeObserver(handleResize);
    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }

    return () => {
      resizeObserver.disconnect();
    };
  }, [height]);

  // Pause / Resume handler
  const handleTogglePause = () => {
    if (!isPaused) {
      // Snapshot current buffer state for frozen inspection
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

    const timeDeltaSec = ((pointOffset - cap) / 60);

    setHoverData({
      x: mouseX,
      speed: Math.round(sourceSpeed[bufferPos]),
      gas: Math.round(sourceGas[bufferPos] * 100),
      brake: Math.round(sourceBrake[bufferPos] * 100),
      steer: parseFloat((sourceSteer[bufferPos] * 180).toFixed(1)),
      rpm: Math.round(sourceRpm[bufferPos]),
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
              <span className={`absolute inline-flex h-full w-full animate-ping rounded-full ${isPaused ? "bg-amber-400" : "bg-emerald-400"} opacity-75`}></span>
              <span className={`relative inline-flex h-3 w-3 rounded-full ${isPaused ? "bg-amber-500" : "bg-emerald-500"}`}></span>
            </span>
            <h2 className="text-sm font-semibold tracking-wider text-gray-200 uppercase">
              Live Telemetry Stream (60 Hz Canvas)
            </h2>
          </div>

          {showFps && (
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
            className={`rounded px-2 py-1 font-medium transition ${
              visibleChannels.throttle
                ? "bg-emerald-500/20 text-emerald-400 border border-emerald-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            Throttle (Gas)
          </button>
          <button
            onClick={() => toggleChannel("brake")}
            className={`rounded px-2 py-1 font-medium transition ${
              visibleChannels.brake
                ? "bg-rose-500/20 text-rose-400 border border-rose-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            Brake
          </button>
          <button
            onClick={() => toggleChannel("speed")}
            className={`rounded px-2 py-1 font-medium transition ${
              visibleChannels.speed
                ? "bg-cyan-500/20 text-cyan-400 border border-cyan-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            Speed
          </button>
          <button
            onClick={() => toggleChannel("steer")}
            className={`rounded px-2 py-1 font-medium transition ${
              visibleChannels.steer
                ? "bg-amber-500/20 text-amber-400 border border-amber-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            Steering
          </button>
          <button
            onClick={() => toggleChannel("rpm")}
            className={`rounded px-2 py-1 font-medium transition ${
              visibleChannels.rpm
                ? "bg-purple-500/20 text-purple-400 border border-purple-500/40"
                : "bg-gray-800/50 text-gray-500 border border-transparent"
            }`}
          >
            RPM
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
                  (containerRef.current?.clientWidth || 900) - 260
                )}px`,
              }}
            >
              <div className="text-gray-400">{hoverData.timeDeltaSec}s</div>
              {visibleChannels.speed && (
                <div className="text-cyan-400 font-bold">{hoverData.speed} km/h</div>
              )}
              {visibleChannels.throttle && (
                <div className="text-emerald-400 font-bold">G: {hoverData.gas}%</div>
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

  // Divide canvas into 2 visual zones:
  // Upper zone: Speed & RPM (0% to 50% height)
  // Lower zone: Throttle, Brake & Steering (52% to 100% height)
  const zoneSplitY = height * 0.5;

  // Grid lines and scales
  ctx.lineWidth = 1;
  ctx.strokeStyle = "#161d2b";

  // Horizontal guidelines
  const hLines = [0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875];
  for (const ratio of hLines) {
    const y = Math.floor(height * ratio);
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }

  // Zone separator
  ctx.strokeStyle = "#253147";
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  ctx.moveTo(0, zoneSplitY);
  ctx.lineTo(width, zoneSplitY);
  ctx.stroke();

  // Vertical time division lines (every 2 seconds)
  const vDivisions = 10;
  ctx.strokeStyle = "#141a24";
  ctx.lineWidth = 1;
  for (let i = 1; i < vDivisions; i++) {
    const x = Math.floor((width / vDivisions) * i);
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
  }

  // Axis Labels & Legends in background
  ctx.font = "10px JetBrains Mono, monospace";
  ctx.fillStyle = "#4b5563";
  ctx.fillText("300 km/h", 8, 18);
  ctx.fillText("150 km/h", 8, zoneSplitY * 0.5 + 4);
  ctx.fillText("0 km/h", 8, zoneSplitY - 6);

  ctx.fillText("100% Pedals", 8, zoneSplitY + 18);
  ctx.fillText("50%", 8, zoneSplitY + (height - zoneSplitY) * 0.5 + 4);
  ctx.fillText("0%", 8, height - 6);

  const stepX = width / (cap - 1);

  // 1. Draw RPM (Purple dashed) in upper zone
  if (visible.rpm) {
    ctx.strokeStyle = "#a855f7";
    ctx.lineWidth = 1.2;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = rpmData[idx]; // 0..9000
      const norm = Math.min(1.0, Math.max(0.0, val / 9000));
      const x = i * stepX;
      const y = zoneSplitY - norm * (zoneSplitY - 12);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.setLineDash([]);
  }

  // 2. Draw Speed (Cyan) in upper zone
  if (visible.speed) {
    ctx.strokeStyle = "#06b6d4";
    ctx.lineWidth = 2.2;
    ctx.shadowColor = "#06b6d4";
    ctx.shadowBlur = 4;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = speedData[idx]; // 0..300 km/h
      const norm = Math.min(1.0, Math.max(0.0, val / 300));
      const x = i * stepX;
      const y = zoneSplitY - norm * (zoneSplitY - 12);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // Lower Zone Base Parameters
  const lowerHeight = height - zoneSplitY;

  // 3. Draw Steering Angle (Yellow/Amber) in lower zone (centered around middle of lower zone)
  if (visible.steer) {
    ctx.strokeStyle = "#eab308";
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    const steerCenterY = zoneSplitY + lowerHeight * 0.5;
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = steerData[idx]; // -1.0 .. +1.0
      const x = i * stepX;
      const y = steerCenterY - val * (lowerHeight * 0.42);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
  }

  // 4. Draw Gas / Throttle (Neon Green) in lower zone
  if (visible.throttle) {
    ctx.strokeStyle = "#22c55e";
    ctx.lineWidth = 2.0;
    ctx.shadowColor = "#22c55e";
    ctx.shadowBlur = 3;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = gasData[idx]; // 0.0 .. 1.0
      const x = i * stepX;
      const y = height - 4 - val * (lowerHeight - 16);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.shadowBlur = 0;
  }

  // 5. Draw Brake (Racing Red) in lower zone
  if (visible.brake) {
    ctx.strokeStyle = "#ef4444";
    ctx.lineWidth = 2.0;
    ctx.shadowColor = "#ef4444";
    ctx.shadowBlur = 3;
    ctx.beginPath();
    for (let i = 0; i < cap; i++) {
      const idx = (startIdx + i) % cap;
      const val = brakeData[idx]; // 0.0 .. 1.0
      const x = i * stepX;
      const y = height - 4 - val * (lowerHeight - 16);
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
