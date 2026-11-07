"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import {
  FastTelemetryPacket,
  TacticalEvent,
  WebSocketServerMessage,
  WebSocketClientMessage,
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

  const wsRef = useRef<WebSocket | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const pingIntervalRef = useRef<NodeJS.Timeout | null>(null);
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
        setServerStatus("Connected (60 FPS Stream)");

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
          } else if (data.type === "TACTICAL_EVENT") {
            if (onEventRef.current) {
              onEventRef.current(data);
            }
          } else if (data.type === "PONG") {
            const now = Date.now();
            setLastPing(Math.max(1, now - data.timestamp));
          }
        } catch (e) {
          // Skip malformed packets without breaking the stream
        }
      };

      ws.onclose = () => {
        setIsConnected(false);
        setIsConnecting(false);
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
    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }
    setIsConnected(false);
    setIsConnecting(false);
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

  useEffect(() => {
    shouldReconnectRef.current = autoConnect;
    if (autoConnect) {
      connect();
    }
    return () => {
      shouldReconnectRef.current = false;
      if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
      if (pingIntervalRef.current) clearInterval(pingIntervalRef.current);
      if (wsRef.current) {
        wsRef.current.close();
      }
    };
  }, [autoConnect, connect]);

  return {
    isConnected,
    isConnecting,
    serverStatus,
    lastPing,
    buffers: buffers.current,
    connect,
    disconnect,
    sendEvent,
  };
}
