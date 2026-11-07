"use client";

import React, { useEffect, useState } from "react";
import { TelemetryBuffers } from "../../hooks/useTelemetryWebSocket";
import { Gauge, Activity, Fuel, Compass, Flag } from "lucide-react";

interface TelemetryGaugesProps {
  buffers: TelemetryBuffers;
}

export const TelemetryGauges: React.FC<TelemetryGaugesProps> = ({ buffers }) => {
  // We read latest metrics at an interval (~15-20Hz) to update DOM badges without choking React
  const [metrics, setMetrics] = useState({
    speed: 0,
    gas: 0,
    brake: 0,
    steer: 0,
    gear: 0,
    rpm: 0,
    distNorm: 0,
    lap: 1,
    sector: 0,
    fuel: 52.4,
    latG: 0,
    longG: 0,
  });

  useEffect(() => {
    const timer = setInterval(() => {
      const latest = buffers.latestPacket.current;
      if (latest) {
        setMetrics({
          speed: Math.round(latest.speed),
          gas: Math.round(latest.gas * 100),
          brake: Math.round(latest.brake * 100),
          steer: parseFloat((latest.steer * 180).toFixed(1)),
          gear: latest.gear,
          rpm: latest.rpm,
          distNorm: parseFloat(latest.distNorm.toFixed(3)),
          lap: latest.lap,
          sector: latest.currentSector,
          fuel: latest.fuel ?? 50.0,
          latG: latest.latG,
          longG: latest.longG,
        });
      }
    }, 50); // 20 FPS UI DOM poll is optimal for CPU while Canvas does the heavy 60 FPS lift

    return () => clearInterval(timer);
  }, [buffers]);

  const rpmPercent = Math.min(100, Math.max(0, (metrics.rpm / 8500) * 100));
  const isShiftWarning = metrics.rpm > 7300;

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
      {/* 1. Gear & RPM Indicator */}
      <div className="col-span-1 flex flex-col items-center justify-center rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">Current Gear</span>
        <div className="my-1 flex items-baseline gap-1">
          <span className="text-4xl font-black font-mono text-cyan-400">
            {metrics.gear === -1 ? "R" : metrics.gear === 0 ? "N" : metrics.gear}
          </span>
        </div>
        <div className="w-full">
          <div className="flex justify-between text-[10px] font-mono text-gray-500">
            <span>RPM</span>
            <span className={isShiftWarning ? "font-bold text-rose-400 animate-pulse" : "text-gray-300"}>
              {metrics.rpm}
            </span>
          </div>
          <div className="mt-1 h-2 w-full overflow-hidden rounded-full bg-gray-900 border border-gray-800">
            <div
              className={`h-full transition-all duration-75 ${
                isShiftWarning ? "bg-rose-500 shadow-[0_0_8px_#f43f5e]" : "bg-purple-500"
              }`}
              style={{ width: `${rpmPercent}%` }}
            />
          </div>
        </div>
      </div>

      {/* 2. Speedometer */}
      <div className="col-span-1 flex flex-col justify-between rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">Speed</span>
          <Gauge className="h-4 w-4 text-cyan-400" />
        </div>
        <div className="my-1 flex items-baseline gap-1">
          <span className="text-3xl font-extrabold font-mono text-white">{metrics.speed}</span>
          <span className="text-xs font-mono text-gray-400">km/h</span>
        </div>
        <div className="h-1.5 w-full overflow-hidden rounded-full bg-gray-900">
          <div
            className="h-full bg-cyan-400 transition-all duration-75"
            style={{ width: `${Math.min(100, (metrics.speed / 300) * 100)}%` }}
          />
        </div>
      </div>

      {/* 3. Throttle & Brake Pedals */}
      <div className="col-span-1 flex flex-col justify-between rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">Pedals</span>
        <div className="my-1 flex items-center justify-around gap-2">
          {/* Brake Bar */}
          <div className="flex flex-col items-center gap-1 w-1/2">
            <div className="relative h-12 w-3 rounded-full bg-gray-900 overflow-hidden border border-gray-800">
              <div
                className="absolute bottom-0 w-full bg-rose-500 transition-all duration-75"
                style={{ height: `${metrics.brake}%` }}
              />
            </div>
            <span className="text-[10px] font-mono font-bold text-rose-400">B {metrics.brake}%</span>
          </div>
          {/* Throttle Bar */}
          <div className="flex flex-col items-center gap-1 w-1/2">
            <div className="relative h-12 w-3 rounded-full bg-gray-900 overflow-hidden border border-gray-800">
              <div
                className="absolute bottom-0 w-full bg-emerald-500 transition-all duration-75"
                style={{ height: `${metrics.gas}%` }}
              />
            </div>
            <span className="text-[10px] font-mono font-bold text-emerald-400">T {metrics.gas}%</span>
          </div>
        </div>
      </div>

      {/* 4. G-Force & Steering */}
      <div className="col-span-1 flex flex-col justify-between rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">G-Force & Steer</span>
          <Activity className="h-4 w-4 text-amber-400" />
        </div>
        <div className="my-1 grid grid-cols-2 gap-1 text-center font-mono">
          <div className="rounded bg-black/40 p-1">
            <span className="text-[10px] text-gray-500 block">Lat G</span>
            <span className="text-xs font-bold text-amber-400">
              {metrics.latG > 0 ? `+${metrics.latG}` : metrics.latG}G
            </span>
          </div>
          <div className="rounded bg-black/40 p-1">
            <span className="text-[10px] text-gray-500 block">Long G</span>
            <span className="text-xs font-bold text-cyan-400">
              {metrics.longG > 0 ? `+${metrics.longG}` : metrics.longG}G
            </span>
          </div>
        </div>
        <div className="text-[11px] font-mono text-gray-400 flex justify-between">
          <span>Steer:</span>
          <span className="text-amber-400 font-bold">{metrics.steer}°</span>
        </div>
      </div>

      {/* 5. Lap & Sector Status */}
      <div className="col-span-1 flex flex-col justify-between rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">Session Lap</span>
          <Flag className="h-4 w-4 text-emerald-400" />
        </div>
        <div className="my-1 flex items-baseline gap-2">
          <span className="text-2xl font-bold font-mono text-white">Lap {metrics.lap}</span>
          <span className="text-xs font-mono text-emerald-400 font-bold">Sec {metrics.sector + 1}</span>
        </div>
        <div className="w-full">
          <div className="flex justify-between text-[10px] font-mono text-gray-500">
            <span>Lap Progress</span>
            <span>{Math.round(metrics.distNorm * 100)}%</span>
          </div>
          <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-gray-900">
            <div
              className="h-full bg-emerald-400 transition-all duration-100"
              style={{ width: `${metrics.distNorm * 100}%` }}
            />
          </div>
        </div>
      </div>

      {/* 6. Fuel & Efficiency */}
      <div className="col-span-1 flex flex-col justify-between rounded-xl border border-gray-800 bg-[#0d1117] p-3 shadow-lg">
        <div className="flex items-center justify-between">
          <span className="text-[11px] font-mono uppercase tracking-wider text-gray-400">Fuel Tank</span>
          <Fuel className="h-4 w-4 text-amber-500" />
        </div>
        <div className="my-1 flex items-baseline gap-1">
          <span className="text-2xl font-bold font-mono text-white">{metrics.fuel}</span>
          <span className="text-xs font-mono text-gray-400">Liters</span>
        </div>
        <div className="flex items-center justify-between text-[10px] font-mono text-gray-400">
          <span>Est. Laps:</span>
          <span className="font-bold text-amber-400">{Math.floor(metrics.fuel / 2.85)} laps</span>
        </div>
      </div>
    </div>
  );
};
