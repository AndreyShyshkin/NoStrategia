"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import {
  FastTelemetryPacket,
  TacticalEvent,
  WebSocketServerMessage,
  WebSocketClientMessage,
  GatewayStatusMessage,
  SessionPhase,
  formatCarModel,
  formatTrackName,
} from "../types/telemetry";

export interface TelemetryBuffers {
  capacity: number;
  speed: Float32Array;
  gas: Float32Array;
  brake: Float32Array;
  steer: Float32Array;
  rpm: Float32Array;
  distNorm: Float32Array;
  timestamps: Float64Array;
  headIndex: { current: number };
  totalPacketsReceived: { current: number };
  latestPacket: { current: FastTelemetryPacket | null };
}

export interface UseTelemetryWebSocketOptions {
  url?: string;
  bufferCapacity?: number;
  autoConnect?: boolean;
  onEvent?: (event: TacticalEvent) => void;
}

export function useTelemetryWebSocket({
  url = process.env.NEXT_PUBLIC_WS_URL || "ws://localhost:4001/ws/stream",
  bufferCapacity = 1200, // 20 seconds at 60 Hz
  autoConnect = true,
  onEvent,
}: UseTelemetryWebSocketOptions = {}) {
  const [isConnected, setIsConnected] = useState<boolean>(false);
  const [isConnecting, setIsConnecting] = useState<boolean>(false);
  const [lastPing, setLastPing] = useState<number>(0);
  const [serverStatus, setServerStatus] = useState<string>("Disconnected");

  // Advanced Agent & Game Session tracking matching TelemetryAgent
  const [streamSource, setStreamSource] = useState<"agent" | "waiting_for_agent" | "simulator">("waiting_for_agent");
  const [isLive, setIsLive] = useState<boolean>(false);
  const [agentConnected, setAgentConnected] = useState<boolean>(false);
  const [sessionPhase, setSessionPhase] = useState<SessionPhase>("WAITING_FOR_AGENT");
  const [phaseDetails, setPhaseDetails] = useState<string>("Очікування запуску Telemetry Agent...");
  const [showCarAndTrack, setShowCarAndTrack] = useState<boolean>(false);
  const [carModel, setCarModel] = useState<string>("");
  const [track, setTrack] = useState<string>("");
  const [simulationEnabled, setSimulationEnabled] = useState<boolean>(false);

  const wsRef = useRef<WebSocket | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const pingIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const liveInactivityTimerRef = useRef<NodeJS.Timeout | null>(null);
  const shouldReconnectRef = useRef<boolean>(autoConnect);
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  // Zero-GC circular ring buffers stored in refs
  const buffers = useRef<TelemetryBuffers>({
    capacity: bufferCapacity,
    speed: new Float32Array(bufferCapacity),
    gas: new Float32Array(bufferCapacity),
    brake: new Float32Array(bufferCapacity),
    steer: new Float32Array(bufferCapacity),
    rpm: new Float32Array(bufferCapacity),
    distNorm: new Float32Array(bufferCapacity),
    timestamps: new Float64Array(bufferCapacity),
    headIndex: { current: 0 },
    totalPacketsReceived: { current: 0 },
    latestPacket: { current: null },
  });

  const connect = useCallback(() => {
    if (wsRef.current && (wsRef.current.readyState === WebSocket.OPEN || wsRef.current.readyState === WebSocket.CONNECTING)) {
      return;
    }

    setIsConnecting(true);
    setServerStatus("Connecting...");

    try {
      const ws = new WebSocket(url);
      wsRef.current = ws;

      ws.onopen = () => {
        setIsConnected(true);
        setIsConnecting(false);
        setServerStatus("Connected to Gateway");

        // Send subscription command
        const subMsg: WebSocketClientMessage = {
          action: "subscribe",
          channels: ["telemetry:fast", "strategy:tactics"],
        };
        ws.send(JSON.stringify(subMsg));

        // Start ping interval
        if (pingIntervalRef.current) clearInterval(pingIntervalRef.current);
        pingIntervalRef.current = setInterval(() => {
          if (ws.readyState === WebSocket.OPEN) {
            const ping: WebSocketClientMessage = { action: "ping", timestamp: Date.now() };
            ws.send(JSON.stringify(ping));
          }
        }, 5000);
      };

      ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data) as WebSocketServerMessage;

          if (data.type === "FAST_PACKET") {
            setIsLive(true);
            setAgentConnected(true);

            // Determine phase from incoming packet
            if (data.status === 0) {
              setSessionPhase("IN_LOBBY");
              setPhaseDetails("Гра в лобі / вибір автомобіля");
              setShowCarAndTrack(false);
              setCarModel("");
              setTrack("");
            } else {
              if (data.isInPitLane === 1) {
                setSessionPhase("IN_PIT_LANE");
                setPhaseDetails("Піт-лейн (обмежувач швидкості активний, 10 Гц)");
              } else if (data.isInPit === 1 || (data.speed < 1.0 && data.gas === 0 && data.brake === 0)) {
                setSessionPhase("IN_GARAGE");
                setPhaseDetails("Бокси / Меню налаштування сетапу автомобіля");
              } else if (data.status === 3) {
                setSessionPhase("SESSION_PAUSED");
                setPhaseDetails("Сесію призупинено в меню паузи");
              } else if (data.status === 1) {
                setSessionPhase("REPLAY");
                setPhaseDetails("Перегляд повтору (Replay Mode)");
              } else {
                setSessionPhase("LIVE_ON_TRACK");
                setPhaseDetails("Assetto Corsa Competizione (Бойовий заїзд 60 Гц)");
              }

              setShowCarAndTrack(true);
              if (data.carModel && data.carModel !== "unknown") {
                setCarModel(formatCarModel(data.carModel));
              }
              if (data.track && data.track !== "unknown") {
                setTrack(formatTrackName(data.track));
              }
            }

            // Reset inactivity timer (after 2.5s of no packets, player is in lobby)
            if (liveInactivityTimerRef.current) clearTimeout(liveInactivityTimerRef.current);
            liveInactivityTimerRef.current = setTimeout(() => {
              setIsLive(false);
              setSessionPhase("IN_LOBBY");
              setPhaseDetails("Агент підключений • Гра в лобі / вибір авто");
              setShowCarAndTrack(false);
              setCarModel("");
              setTrack("");
            }, 2500);

            const b = buffers.current;
            const idx = b.headIndex.current % b.capacity;

            b.speed[idx] = data.speed;
            b.gas[idx] = data.gas;
            b.brake[idx] = data.brake;
            b.steer[idx] = data.steer;
            b.rpm[idx] = data.rpm;
            b.distNorm[idx] = data.distNorm;
            b.timestamps[idx] = data.timestamp;

            b.headIndex.current++;
            b.totalPacketsReceived.current++;
            b.latestPacket.current = data;
          } else if (data.type === "GATEWAY_STATUS") {
            setStreamSource(data.source);
            setAgentConnected(data.agentConnected);
            setSimulationEnabled(data.simulationEnabled);
            setSessionPhase(data.sessionPhase);
            setPhaseDetails(data.phaseDetails);
            setShowCarAndTrack(data.showCarAndTrack);

            if (data.showCarAndTrack && data.carModel) {
              setCarModel(formatCarModel(data.carModel));
              setTrack(formatTrackName(data.track));
            } else if (!data.showCarAndTrack) {
              setCarModel("");
              setTrack("");
            }

            if (!data.isLive) {
              setIsLive(false);
            }
          } else if (data.type === "TACTICAL_EVENT") {
            if (onEventRef.current) {
              onEventRef.current(data);
            }
          } else if (data.type === "PONG") {
            const now = Date.now();
            setLastPing(Math.max(1, now - data.timestamp));
          }
        } catch (e) {
          // Skip malformed packets
        }
      };

      ws.onclose = () => {
        setIsConnected(false);
        setIsConnecting(false);
        setIsLive(false);
        setSessionPhase("WAITING_FOR_AGENT");
        setShowCarAndTrack(false);
        setServerStatus("Disconnected");
        if (pingIntervalRef.current) clearInterval(pingIntervalRef.current);

        if (shouldReconnectRef.current) {
          reconnectTimeoutRef.current = setTimeout(() => {
            connect();
          }, 2000);
        }
      };

      ws.onerror = () => {
        setServerStatus("Connection Error");
      };
    } catch {
      setIsConnecting(false);
      setServerStatus("Connection Failed");
    }
  }, [url]);

  const disconnect = useCallback(() => {
    shouldReconnectRef.current = false;
    if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
    if (pingIntervalRef.current) clearInterval(pingIntervalRef.current);
    if (liveInactivityTimerRef.current) clearTimeout(liveInactivityTimerRef.current);
    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }
    setIsConnected(false);
    setIsConnecting(false);
    setIsLive(false);
    setSessionPhase("WAITING_FOR_AGENT");
    setShowCarAndTrack(false);
    setServerStatus("Disconnected");
  }, []);

  const sendEvent = useCallback((text: string, priority: number) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
      wsRef.current.send(
        JSON.stringify({
          action: "trigger_event",
          text,
          priority,
        })
      );
    }
  }, []);

  const toggleSimulation = useCallback((enabled: boolean) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
      wsRef.current.send(
        JSON.stringify({
          action: "set_simulation",
          enabled,
        })
      );
    }
    setSimulationEnabled(enabled);
  }, []);

  useEffect(() => {
    shouldReconnectRef.current = autoConnect;
    if (autoConnect) {
      connect();
    }
    return () => {
      shouldReconnectRef.current = false;
      if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
      if (pingIntervalRef.current) clearInterval(pingIntervalRef.current);
      if (liveInactivityTimerRef.current) clearTimeout(liveInactivityTimerRef.current);
      if (wsRef.current) {
        wsRef.current.close();
      }
    };
  }, [autoConnect, connect]);

  return {
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
    buffers: buffers.current,
    connect,
    disconnect,
    sendEvent,
    toggleSimulation,
  };
}
